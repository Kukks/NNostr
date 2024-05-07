using System.Net.WebSockets;

namespace NNostr.Client;

public class CompositeNostrClient: INostrClient
{
    private readonly NostrClient[] _clients;

    public Dictionary<Uri, WebSocketState?> States => _clients.ToDictionary(c => c.Relay, c => c.State);
    public CompositeNostrClient(Uri[] relays, Action<WebSocket>? websocketConfigure = null)
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
            c.StateChanged += (sender, state) => StateChanged?.Invoke(sender, (r, state));;
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
            client.Dispose();       
        }
    }
    
    public Task ConnectAndWaitUntilConnected(CancellationToken connectionCancellationToken = default, CancellationToken lifetimeCancellationToken = default)
    {
        return Task.WhenAll(_clients.Select(c => c.ConnectAndWaitUntilConnected(connectionCancellationToken, lifetimeCancellationToken)));
    }

    public event EventHandler<string>? MessageReceived;
    public event EventHandler<string>? InvalidMessageReceived;
    public event EventHandler<string>? NoticeReceived;
    public event EventHandler<(string subscriptionId, NostrEvent[] events)>? EventsReceived;
    public event EventHandler<(string eventId, bool success, string messafe)>? OkReceived;
    public event EventHandler<string>? EoseReceived;
    public event EventHandler<(Uri,WebSocketState?)>? StateChanged;
}