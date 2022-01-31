using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Relay;

namespace NNostr.Client
{
    public class NostrClient : IDisposable
    {
        private readonly Uri _relay;
        protected ClientWebSocket? websocket;
        private CancellationTokenSource? _Cts;
        private CancellationTokenSource messageCts = new();
        public NostrClient(Uri relay)
        {
            _relay = relay;
            _ = ProcessSendMessages(messageCts.Token);
        }

        public Task Disconnect()
        {
            _Cts?.Cancel();
            return Task.CompletedTask;
        }

        public EventHandler<string> MessageReceived;
        public EventHandler<string> NoticeReceived;
        public EventHandler<(string subscriptionId, NostrEvent[] events)> EventsReceived;

        public async Task Connect(CancellationToken token = default)
        {
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(token);

            while (!_Cts.IsCancellationRequested)
            {
                await ConnectAndWaitUntilConnected(_Cts.Token);
                _ = ListenForMessages();
                websocket!.Abort();
            }
        }

        public async Task ListenForMessages()
        {
            while (websocket!.State == WebSocketState.Open && !_Cts!.IsCancellationRequested)
            {
                var buffer = new ArraySegment<byte>(new byte[1024 * 4]);
                try
                {
                    string? message = null;
                    WebSocketReceiveResult? result = null;
                    await using (var ms = new MemoryStream())
                    {
                        do
                        {
                            result = await websocket.ReceiveAsync(buffer, _Cts.Token)
                                .ConfigureAwait(false);
                            ms.Write(buffer.Array!, buffer.Offset, result.Count);
                        } while (!result.EndOfMessage);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            websocket.Abort();
                            break;
                        }

                        ms.Seek(0, SeekOrigin.Begin);

                        using (var reader = new StreamReader(ms, Encoding.UTF8))
                        {
                            message = await reader.ReadToEndAsync().ConfigureAwait(false);
                        }
                    }

                    await PendingMessages.Writer.WriteAsync(message);
                    MessageReceived.Invoke(this, message);
                }
                catch (WebSocketException e)
                {
                }
            }
        }

        private readonly Channel<string> PendingMessages = Channel.CreateUnbounded<string>();

        private Task HandleMessage(string message)
        {
            var json = JsonDocument.Parse(message).RootElement;
            switch (json[0].GetRawText().ToLowerInvariant())
            {
                case "event":
                    var subscriptionId = json[1].GetRawText();
                    var evt = json[2].Deserialize<NostrEvent>();
                    if (evt?.Verify() is true)
                    {
                        EventsReceived.Invoke(this, (subscriptionId, new []{evt}));
                    }
                    break;
                case "notice":
                    var noticeMessage = json[1].GetRawText();
                    NoticeReceived.Invoke(this, noticeMessage);
                    break;
            }

            return Task.CompletedTask;
        }
        
        private async Task ProcessSendMessages(CancellationToken cancellationToken)
        {
            while (await PendingMessages.Reader.WaitToReadAsync(cancellationToken))
            {
                if (PendingMessages.Reader.TryRead(out var evt))
                {
                    await HandleMessage(evt);
                }
            }
        }

        public async Task PublishEvent(NostrEvent nostrEvent, CancellationToken token = default)
        {
            var payload = JsonSerializer.Serialize(new[] { "EVENT", nostrEvent.ToJson() });
            await websocket.SendMessageAsync(payload, token);
        }

        public async Task CloseSubscription(string subscriptionId, CancellationToken token = default)
        {
            var payload = JsonSerializer.Serialize(new[] { "CLOSE", subscriptionId });
            await websocket.SendMessageAsync(payload, token);
        }

        public async Task CreateSubscription(string subscriptionId, NostrSubscriptionFilter[] filters, CancellationToken token = default)
        {
            var payload = JsonSerializer.Serialize(new object[] { "REQ", subscriptionId}.Concat(filters));
            await websocket.SendMessageAsync(payload, token);
        }

        public void Dispose()
        {
            messageCts.Cancel();
            Disconnect();
        }

        public async Task ConnectAndWaitUntilConnected(CancellationToken token)
        {
            if (websocket?.State == WebSocketState.Open)
            {
                return;
            }
            websocket?.Dispose();
            websocket = new ClientWebSocket();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(5000);
            await websocket.ConnectAsync(_relay, cts.Token);
            while (websocket.State != WebSocketState.Open && !cts.IsCancellationRequested)
            {
                await Task.Delay(100, cts.Token);
            }
        }
    }
}