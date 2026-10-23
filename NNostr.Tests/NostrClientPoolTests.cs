using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NNostr.Client;
using Xunit;

namespace NNostr.Tests;

public class NostrClientPoolTests
{
    private static readonly Uri[] Relays = { new("wss://relay.example.com") };

    [Fact]
    public void CleanupExpiredClients_DoesNotDisposeClientWithActiveLease()
    {
        var client = new FakeNostrClient();
        using var pool = new TestableNostrClientPool(() => client);

        var (_, lease) = pool.GetClient(Relays);
        var wrapper = Assert.IsType<NostrClientPool.UsageDisposable>(lease).ClientWrapper;

        // Pretend the client has been idle for longer than the expiration window while
        // it is still leased. The cleanup must not dispose it.
        wrapper._lastUsed = DateTimeOffset.UtcNow.AddMinutes(-10);
        pool.CleanupExpiredClients(null);

        Assert.Equal(0, client.DisposeCount);

        lease.Dispose();
        wrapper._lastUsed = DateTimeOffset.UtcNow.AddMinutes(-10);
        pool.CleanupExpiredClients(null);

        Assert.Equal(1, client.DisposeCount);
    }

    [Fact]
    public void CleanupExpiredClients_DoesNotDisposeClientAcquiredAfterExpiry()
    {
        var client = new FakeNostrClient();
        using var pool = new TestableNostrClientPool(() => client);

        var (firstClient, firstLease) = pool.GetClient(Relays);
        var wrapper = Assert.IsType<NostrClientPool.UsageDisposable>(firstLease).ClientWrapper;
        firstLease.Dispose();

        // The wrapper looks expired, but acquiring it must refresh its last-used
        // timestamp under the pool lock before the cleanup sweep runs.
        wrapper._lastUsed = DateTimeOffset.UtcNow.AddMinutes(-10);
        var (secondClient, secondLease) = pool.GetClient(Relays);

        Assert.Same(firstClient, secondClient);
        pool.CleanupExpiredClients(null);
        Assert.Equal(0, client.DisposeCount);

        secondLease.Dispose();
    }

    [Fact]
    public void KillClient_DefersDisposalUntilLastLeaseIsReleased()
    {
        var clients = new List<FakeNostrClient>();
        using var pool = new TestableNostrClientPool(() =>
        {
            var client = new FakeNostrClient();
            clients.Add(client);
            return client;
        });

        var (firstClient, firstLease) = pool.GetClient(Relays);
        var (secondClient, secondLease) = pool.GetClient(Relays);
        Assert.Same(firstClient, secondClient);
        Assert.Equal(2, Assert.IsType<NostrClientPool.UsageDisposable>(secondLease).ClientWrapper.UsageCount);

        pool.KillClient(Relays);

        // The client is removed from the pool, but two callers still hold leases.
        Assert.Equal(0, clients[0].DisposeCount);

        firstLease.Dispose();
        Assert.Equal(0, clients[0].DisposeCount);

        secondLease.Dispose();
        Assert.Equal(1, clients[0].DisposeCount);

        // The killed client must not be handed out again.
        var (thirdClient, thirdLease) = pool.GetClient(Relays);
        Assert.NotSame(firstClient, thirdClient);
        Assert.Equal(2, clients.Count);

        thirdLease.Dispose();
    }

    [Fact]
    public void CleanupExpiredClients_RemovesExpiredClientSoNextAcquisitionCreatesFreshOne()
    {
        var clients = new List<FakeNostrClient>();
        using var pool = new TestableNostrClientPool(() =>
        {
            var client = new FakeNostrClient();
            clients.Add(client);
            return client;
        });

        var (firstClient, lease) = pool.GetClient(Relays);
        var wrapper = Assert.IsType<NostrClientPool.UsageDisposable>(lease).ClientWrapper;
        lease.Dispose();
        wrapper._lastUsed = DateTimeOffset.UtcNow.AddMinutes(-10);

        pool.CleanupExpiredClients(null);
        Assert.Equal(1, clients[0].DisposeCount);

        var (secondClient, secondLease) = pool.GetClient(Relays);
        Assert.NotSame(firstClient, secondClient);
        Assert.Equal(2, clients.Count);

        secondLease.Dispose();
    }

    private sealed class TestableNostrClientPool : NostrClientPool
    {
        private readonly Func<INostrClient> _clientFactory;

        public TestableNostrClientPool(Func<INostrClient> clientFactory, TimeSpan? unusedClientTimeout = null)
            : base(unusedClientTimeout)
        {
            _clientFactory = clientFactory;
        }

        protected override NostrClientWrapper CreateClientWrapper(Uri[] relays)
        {
            return new NostrClientWrapper(_clientFactory());
        }
    }

    private sealed class FakeNostrClient : INostrClient
    {
        public int DisposeCount { get; private set; }

        public Task Disconnect() => Task.CompletedTask;

        public Task Connect(CancellationToken token = default) => Task.CompletedTask;

        public async IAsyncEnumerable<string> ListenForRawMessages()
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task ListenForMessages() => Task.CompletedTask;

        public Task PublishEvent(NostrEvent nostrEvent, CancellationToken token = default) => Task.CompletedTask;

        public Task CloseSubscription(string subscriptionId, CancellationToken token = default) => Task.CompletedTask;

        public Task CreateSubscription(string subscriptionId, NostrSubscriptionFilter[] filters,
            CancellationToken token = default) => Task.CompletedTask;

        public Task ConnectAndWaitUntilConnected(CancellationToken connectionCancellationToken = default,
            CancellationToken lifetimeCancellationToken = default) => Task.CompletedTask;

#pragma warning disable CS0067, CS0414 // Interface events are never raised by this fake.
        public event EventHandler<string>? MessageReceived;
        public event EventHandler<string>? NoticeReceived;
        public event EventHandler<(string subscriptionId, NostrEvent[] events)>? EventsReceived;
        public event EventHandler<(string eventId, bool success, string messafe)>? OkReceived;
        public event EventHandler<string>? EoseReceived;
        public event EventHandler<string> InvalidMessageReceived = null!;
#pragma warning restore CS0067, CS0414

        public void Dispose() => DisposeCount++;
    }
}
