using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace NNostr.Client
{
    public class NostrClient : IDisposable
    {
        protected ClientWebSocket? WebSocket;
        
        private readonly Uri _relay;
        private CancellationTokenSource? _cts;
        private readonly CancellationTokenSource _messageCts = new();

        private readonly Channel<string> _pendingIncomingMessages = 
            Channel.CreateUnbounded<string>(new() { SingleReader = true, SingleWriter = true });

        private readonly Channel<string> _pendingOutgoingMessages = 
            Channel.CreateUnbounded<string>(new() { SingleReader = true });

        public NostrClient(Uri relay)
        {
            _relay = relay;
            _ = ProcessChannel(_pendingIncomingMessages, HandleIncomingMessage, _messageCts.Token);
            _ = ProcessChannel(_pendingOutgoingMessages, HandleOutgoingMessage, _messageCts.Token);
        }

        public Task Disconnect()
        {
            _cts?.Cancel();
            return Task.CompletedTask;
        }

        public EventHandler<string>? MessageReceived;
        public EventHandler<string>? NoticeReceived;
        public EventHandler<(string subscriptionId, NostrEvent[] events)>? EventsReceived;
        public EventHandler<(string eventId, bool success, string messafe)>? OkReceived;
        public EventHandler<string>? EoseReceived;

        public async Task Connect(CancellationToken token = default)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);

            while (!_cts.IsCancellationRequested)
            {
                await ConnectAndWaitUntilConnected(_cts.Token);
                _ = ListenForMessages();
                WebSocket!.Abort();
            }
        }

        public async IAsyncEnumerable<string> ListenForRawMessages()
        {
            Memory<byte> buffer = new byte[2048];
            while (WebSocket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                ValueWebSocketReceiveResult result;
                using var ms = new MemoryStream();
                do
                {
                    result = await WebSocket!.ReceiveAsync(buffer, _cts.Token).ConfigureAwait(false);
                    ms.Write(buffer.Span);
                }
                while (!result.EndOfMessage);

                ms.Seek(0, SeekOrigin.Begin);

                yield return Encoding.UTF8.GetString(ms.ToArray());

                if (result.MessageType == WebSocketMessageType.Close)
                    break;
            }

            WebSocket.Abort();
        }

        public async Task ListenForMessages()
        {
            await foreach (var message in ListenForRawMessages())
            {
                await _pendingIncomingMessages.Writer.WriteAsync(message).ConfigureAwait(false);
                MessageReceived?.Invoke(this, message);
            }
        }

        private Task<bool> HandleIncomingMessage(string message, CancellationToken token)
        {
            var json = JsonDocument.Parse(message).RootElement;
            switch (json[0].GetString().ToLowerInvariant())
            {
                case "event":
                    var subscriptionId = json[1].GetString();
                    var evt = json[2].Deserialize<NostrEvent>();

                    if (evt?.Verify<NostrEvent, NostrEventTag>() is true)
                    {
                        EventsReceived?.Invoke(this, (subscriptionId, new[] {evt}));
                    }

                    break;
                case "notice":
                    var noticeMessage = json[1].GetString();
                    NoticeReceived?.Invoke(this, noticeMessage);
                    break;
                case "eose":
                    subscriptionId = json[1].GetString();
                    EoseReceived?.Invoke(this, subscriptionId);
                    break;
                case "ok":
                    var eventId = json[1].GetString();
                    var success = json[2].GetBoolean();
                    var msg = json.GetArrayLength() == 3 ? json[2].GetString() : String.Empty;
                    OkReceived?.Invoke(this, (eventId, success, msg));
                    break;
            }

            return Task.FromResult(true);
        }

        private async Task<bool> HandleOutgoingMessage(string message, CancellationToken token)
        {
            try
            {
                return await WaitUntilConnected(token)
                    .ContinueWith(_ => WebSocket?.SendMessageAsync(message, token), token)
                    .ContinueWith(_ => true, token);
            }
            catch
            {
                return false;
            }
        }

        private async Task ProcessChannel<T>(Channel<T> channel, Func<T, CancellationToken, Task<bool>> processor,
            CancellationToken cancellationToken)
        {
            while (await channel.Reader.WaitToReadAsync(cancellationToken))
            {
                if (channel.Reader.TryPeek(out var evt))
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    linked.CancelAfter(5000);
                    if (await processor(evt, linked.Token))
                    {
                        channel.Reader.TryRead(out _);
                    }
                }
            }
        }

        public async Task PublishEvent(NostrEvent nostrEvent, CancellationToken token = default)
        {
            var payload = JsonSerializer.Serialize(new object[] {"EVENT", nostrEvent});
            await _pendingOutgoingMessages.Writer.WriteAsync(payload, token);
        }

        public async Task CloseSubscription(string subscriptionId, CancellationToken token = default)
        {
            var payload = JsonSerializer.Serialize(new[] {"CLOSE", subscriptionId});

            await _pendingOutgoingMessages.Writer.WriteAsync(payload, token);
        }

        public async Task CreateSubscription(string subscriptionId, NostrSubscriptionFilter[] filters,
            CancellationToken token = default)
        {
            var payload = JsonSerializer.Serialize(new object[] {"REQ", subscriptionId}.Concat(filters));

            await _pendingOutgoingMessages.Writer.WriteAsync(payload, token);
        }

        public void Dispose()
        {
            _messageCts.Cancel();
            Disconnect();

            _messageCts.Dispose();
            _cts?.Dispose();
        }

        public async Task ConnectAndWaitUntilConnected(CancellationToken token = default)
        {
            if (WebSocket?.State == WebSocketState.Open)
            {
                return;
            }

            _cts ??= CancellationTokenSource.CreateLinkedTokenSource(token);

            WebSocket?.Dispose();
            WebSocket = new ClientWebSocket();
            WebSocket.Options.SetRequestHeader("origin", _relay.ToString());
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            await WebSocket.ConnectAsync(_relay, cts.Token);
            await WaitUntilConnected(cts.Token);
        }

        private async Task WaitUntilConnected(CancellationToken token)
        {
            while (WebSocket != null && WebSocket.State != WebSocketState.Open && !token.IsCancellationRequested)
            {
                await Task.Delay(100, token);
            }
        }
    }
}