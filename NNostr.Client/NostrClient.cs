using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace NNostr.Client
{
    public class NostrClient : INostrClient
    {
        protected WebSocket? WebSocket;

        public readonly Uri Relay;
        private readonly Action<WebSocket>? _websocketConfigure;
        protected CancellationTokenSource? _cts;
        private readonly CancellationTokenSource _messageCts = new();

        public virtual WebSocketState? State => WebSocket?.State;

        private readonly Channel<string> _pendingIncomingMessages =
            Channel.CreateUnbounded<string>(new() {SingleReader = true, SingleWriter = true});

        private readonly Channel<string> _pendingOutgoingMessages =
            Channel.CreateUnbounded<string>(new() {SingleReader = true});

        public NostrClient(Uri relay, Action<WebSocket>? websocketConfigure = null)
        {
            Relay = relay;
            _websocketConfigure = websocketConfigure;
            _ = ProcessChannel(_pendingIncomingMessages, HandleIncomingMessage, _messageCts.Token);
            _ = ProcessChannel(_pendingOutgoingMessages, HandleOutgoingMessage, _messageCts.Token);
        }


        public virtual Task Disconnect()
        {
            _cts?.Cancel();
            return Task.CompletedTask;
        }

        public async Task Connect(CancellationToken token = default)
        {
            await ConnectAndWaitUntilConnected(token);
        }

        public async IAsyncEnumerable<string> ListenForRawMessages()
        {
            while (WebSocket?.State == WebSocketState.Open && _cts?.IsCancellationRequested is false)
            {
                var bufferSize = 1000;
                var buffer = new byte[bufferSize];

                var offset = 0;
                var free = buffer.Length;
                WebSocketReceiveResult result;
                do
                {
                    result = await WebSocket!.ReceiveAsync(new ArraySegment<byte>(buffer, offset, free), _cts.Token)
                        .ConfigureAwait(false);
                    offset += result.Count;
                    free -= result.Count;
                    if (free != 0) continue;
                    // No free space
                    // Resize the outgoing buffer
                    var newSize = buffer.Length + bufferSize;

                    var newBuffer = new byte[newSize];
                    Array.Copy(buffer, 0, newBuffer, 0, offset);
                    buffer = newBuffer;
                    free = buffer.Length - offset;
                } while (!result.EndOfMessage);

                var str = Encoding.UTF8.GetString(buffer, 0, offset);
                yield return str;

                if (result.MessageType == WebSocketMessageType.Close)
                    break;
            }

            WebSocket?.Abort();
        }

        private bool _listening;

        public async Task ListenForMessages()
        {
            if (_listening) return;
            _listening = true;
            try
            {
                await foreach (var message in ListenForRawMessages())
                {
                    await _pendingIncomingMessages.Writer.WriteAsync(message).ConfigureAwait(false);
                    MessageReceived?.Invoke(this, message);
                }
            }
            finally
            {
                _listening = false;
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

            switch (json[0].GetString()?.ToLowerInvariant())
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
                    var msg = json.GetArrayLength() > 3 ? json[3].GetString() : String.Empty;
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
                    .ContinueWith(_ => _.Status == TaskStatus.RanToCompletion, token);
            }
            catch
            {
                return false;
            }
        }

        internal static async Task ProcessChannel<T>(Channel<T> channel, Func<T, CancellationToken, Task<bool>> processor,
            CancellationToken cancellationToken)
        {
            while (await channel.Reader.WaitToReadAsync(cancellationToken))
            {
                if (!channel.Reader.TryPeek(out var evt)) continue;
                var attempts = 0;
                while (attempts < 3)
                {
                    try
                    {
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        linked.CancelAfter(5000);
                        if (await processor(evt, linked.Token))
                        {
                            break;
                        }
                    }
                    catch (Exception)
                    {
                        // ignored
                    }

                    attempts++;
                }

                channel.Reader.TryRead(out _);
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
            _statusListenerTokenSource?.Cancel();
            _messageCts.Dispose();
            _cts?.Dispose();
            _statusListenerTokenSource?.Dispose();
            StateChanged = null;
            MessageReceived = null;
            EventsReceived = null;
            NoticeReceived = null;
            EoseReceived = null;
            OkReceived = null;
            InvalidMessageReceived = null;
        }


        public async Task ConnectAndWaitUntilConnected(CancellationToken connectionCancellationToken = default,
            CancellationToken lifetimeCancellationToken = default)
        {
           
            if (WebSocket?.State == WebSocketState.Open)
            {
                return;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellationToken);

            WebSocket?.Dispose();
            WebSocket = await Connect();
            await WaitUntilConnected(connectionCancellationToken).ContinueWith(task =>
            {
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    _ = ListenForMessages();
                }
            }, _cts.Token);
        }

        protected CancellationTokenSource? _statusListenerTokenSource = null;

        protected virtual async Task<WebSocket> Connect()
        {
            _statusListenerTokenSource?.Cancel();
            _statusListenerTokenSource = new CancellationTokenSource();
            _ = ListenForWebsocketChanges(_statusListenerTokenSource.Token);
            var r = new ClientWebSocket();

            try
            {
                r.Options.SetRequestHeader("origin", Relay.ToString());
            }
            catch (Exception e)
            {
                // ignored
            }

            _websocketConfigure?.Invoke(r);
            await r.ConnectAsync(Relay, _cts.Token);
            return r;
        }

        protected async Task ListenForWebsocketChanges(CancellationToken token)
        {
            WebSocketState? lastState = null;

            while (token.IsCancellationRequested is not true)
            {
                var state = WebSocket?.State;
                if (state != lastState)
                {
                    lastState = state;
                    StateChanged?.Invoke(this, state);
                }

                await Task.Delay(100, token);
            }
        }

        public event EventHandler<WebSocketState?>? StateChanged;

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