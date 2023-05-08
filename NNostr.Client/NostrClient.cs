using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace NNostr.Client
{

    public class CompositeNostrClient: INostrClient
    {
        private readonly NostrClient[] _clients;

        public CompositeNostrClient(Uri[] relays, Action<ClientWebSocket>? websocketConfigure = null)
        {
            _clients = relays.Select(r =>
            {
                var c = new NostrClient(r, websocketConfigure);
                c.MessageReceived += (sender, message) => MessageReceived?.Invoke(sender, message);
                c.InvalidMessageReceived += (sender, message) => InvalidMessageReceived?.Invoke(sender, message);
                c.NoticeReceived += (sender, message) => NoticeReceived?.Invoke(sender, message);
                c.EventsReceived += (sender, events) => EventsReceived?.Invoke(sender, events);
                c.OkReceived += (sender, ok) => OkReceived?.Invoke(sender, ok);
                c.EoseReceived += (sender, message) => EoseReceived?.Invoke(sender, message);
                
                return c;
            }).ToArray();
        }
        public Task Disconnect()
        {
            return Task.WhenAll(_clients.Select(c => c.Disconnect()));
        }

        public Task Connect(CancellationToken token = default)
        {
            return Task.WhenAll(_clients.Select(c => c.Connect(token)));
        }

        public IAsyncEnumerable<string> ListenForRawMessages()
        {
            return _clients.Select(c => c.ListenForRawMessages()).ToArray().Merge();
        }

        public Task ListenForMessages()
        {
            return Task.WhenAll(_clients.Select(c => c.ListenForMessages()));
        }

        public Task PublishEvent(NostrEvent nostrEvent, CancellationToken token = default)
        { 
            return Task.WhenAll(_clients.Select(c => c.PublishEvent(nostrEvent, token)));
        }

        public Task CloseSubscription(string subscriptionId, CancellationToken token = default)
        {
            return Task.WhenAll(_clients.Select(c => c.CloseSubscription(subscriptionId, token)));
        }

        public Task CreateSubscription(string subscriptionId, NostrSubscriptionFilter[] filters, CancellationToken token = default)
        {
            return Task.WhenAll(_clients.Select(c => c.CreateSubscription(subscriptionId, filters, token)));
        }

        public void Dispose()
        {
            foreach (var client in _clients)
            {
                client.MessageReceived -= MessageReceived;
                client.InvalidMessageReceived -= InvalidMessageReceived;
                client.NoticeReceived -= NoticeReceived;
                client.EventsReceived -= EventsReceived;
                client.OkReceived -= OkReceived;
                client.EoseReceived -= EoseReceived;
                
                client.Dispose();
                
            }
        }

        public Task ConnectAndWaitUntilConnected(CancellationToken token = default)
        {
            return Task.WhenAll(_clients.Select(c => c.ConnectAndWaitUntilConnected(token)));
        }

        public event EventHandler<string>? MessageReceived;
        public event EventHandler<string>? InvalidMessageReceived;
        public event EventHandler<string>? NoticeReceived;
        public event EventHandler<(string subscriptionId, NostrEvent[] events)>? EventsReceived;
        public event EventHandler<(string eventId, bool success, string messafe)>? OkReceived;
        public event EventHandler<string>? EoseReceived;
    }


    public interface INostrClient : IDisposable
    {
        Task Disconnect();
        Task Connect(CancellationToken token = default);
        IAsyncEnumerable<string> ListenForRawMessages();
        Task ListenForMessages();
        Task PublishEvent(NostrEvent nostrEvent, CancellationToken token = default);
        Task CloseSubscription(string subscriptionId, CancellationToken token = default);

        Task CreateSubscription(string subscriptionId, NostrSubscriptionFilter[] filters,
            CancellationToken token = default);

        Task ConnectAndWaitUntilConnected(CancellationToken token = default);
        
        event EventHandler<string>? MessageReceived;
        event EventHandler<string> InvalidMessageReceived;
        event EventHandler<string>? NoticeReceived;
        event EventHandler<(string subscriptionId, NostrEvent[] events)>? EventsReceived;
        event EventHandler<(string eventId, bool success, string messafe)>? OkReceived;
        event EventHandler<string>? EoseReceived;
    }

    public class NostrClient : INostrClient
    {
        protected ClientWebSocket? WebSocket;
        
        private readonly Uri _relay;
        private readonly Action<ClientWebSocket>? _websocketConfigure;
        private CancellationTokenSource? _cts;
        private readonly CancellationTokenSource _messageCts = new();

        private readonly Channel<string> _pendingIncomingMessages = 
            Channel.CreateUnbounded<string>(new() { SingleReader = true, SingleWriter = true });

        private readonly Channel<string> _pendingOutgoingMessages = 
            Channel.CreateUnbounded<string>(new() { SingleReader = true });

        public NostrClient(Uri relay, Action<ClientWebSocket>? websocketConfigure = null)
        {
            _relay = relay;
            _websocketConfigure = websocketConfigure;
            _ = ProcessChannel(_pendingIncomingMessages, HandleIncomingMessage, _messageCts.Token);
            _ = ProcessChannel(_pendingOutgoingMessages, HandleOutgoingMessage, _messageCts.Token);
        }

        public Task Disconnect()
        {
            _cts?.Cancel();
            return Task.CompletedTask;
        }

        public async Task Connect(CancellationToken token = default)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);

            while (!_cts.IsCancellationRequested)
            {
                await ConnectAndWaitUntilConnected(_cts.Token);
                await ListenForMessages();
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
            JsonElement json;
            try
            {
                json = JsonDocument.Parse(message.Trim('\0')).RootElement;
            }
            catch (Exception)
            {
                InvalidMessageReceived?.Invoke(this, message);
                return Task.FromResult(true);
            }
            switch (json[0].GetString().ToLowerInvariant())
            {
                case "event":
                    var subscriptionId = json[1].GetString();
                    var evt = json[2].Deserialize<NostrEvent>();

                    if (evt?.Verify() is true)
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
            _websocketConfigure?.Invoke(WebSocket);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            await WebSocket.ConnectAsync(_relay, cts.Token);
            await WaitUntilConnected(cts.Token);
        }

        /// <summary>
        /// All messages received in their raw format from the relay will be sent to this event.
        /// </summary>
        public event EventHandler<string>? MessageReceived;
        /// <summary>
        /// All messages that could not be parsed as JSON will be sent to this event.
        /// </summary>
        public event EventHandler<string>? InvalidMessageReceived;
        /// <summary>
        /// All notices received from the relay will be sent to this event.
        /// </summary>
        public event EventHandler<string>? NoticeReceived;
        /// <summary>
        /// All events received from the relay based on an existing subscription will be sent to this event.
        /// </summary>
        public event EventHandler<(string subscriptionId, NostrEvent[] events)>? EventsReceived;
        /// <summary>
        /// All OK messages received from the relay will be sent to this event.
        /// </summary>
        public event EventHandler<(string eventId, bool success, string messafe)>? OkReceived;
        /// <summary>
        /// All EOSE messages for every active subscription received from the relay will be sent to this event.
        /// </summary>
        public event EventHandler<string>? EoseReceived;

        public async Task WaitUntilConnected(CancellationToken token = default)
        {
            while (WebSocket != null && WebSocket.State != WebSocketState.Open && !token.IsCancellationRequested)
            {
                await Task.Delay(100, token);
            }
        }
    }
}