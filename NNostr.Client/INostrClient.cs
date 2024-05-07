namespace NNostr.Client;

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

    Task ConnectAndWaitUntilConnected(CancellationToken token = default) => ConnectAndWaitUntilConnected(token, token);

    public Task ConnectAndWaitUntilConnected(CancellationToken connectionCancellationToken = default,
        CancellationToken lifetimeCancellationToken = default);
        
    event EventHandler<string>? MessageReceived;
    event EventHandler<string> InvalidMessageReceived;
    event EventHandler<string>? NoticeReceived;
    event EventHandler<(string subscriptionId, NostrEvent[] events)>? EventsReceived;
    event EventHandler<(string eventId, bool success, string messafe)>? OkReceived;
    event EventHandler<string>? EoseReceived;
}