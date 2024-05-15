using System.Collections.Concurrent;

namespace NNostr.Client;


public class NostrClientPool : IDisposable
{
    private readonly ConcurrentDictionary<string, NostrClientWrapper> _clientPool = new();

    private readonly Timer _cleanupTimer;

    public NostrClientPool(TimeSpan? unusedClientTimeout = null)
    {
        unusedClientTimeout ??= TimeSpan.FromMinutes(5);
        _cleanupTimer = new Timer(CleanupExpiredClients, null, unusedClientTimeout.Value, unusedClientTimeout.Value);
    }

    public (INostrClient, IDisposable) GetClient(Uri[] relays)
    {
        if(relays.Length == 0)
            throw new ArgumentException("At least one relay is required", nameof(relays));
       
        var connString = GetConnString(relays);

        var clientWrapper = _clientPool.GetOrAdd(connString,
            k => new NostrClientWrapper(relays.Length > 1? new CompositeNostrClient(relays) : new NostrClient(relays[0])));

        clientWrapper.IncrementUsage();

        return (clientWrapper.Client, new UsageDisposable(clientWrapper));
    }

    public async Task<(INostrClient, IDisposable)> GetClientAndConnect(Uri[] relays, CancellationToken token)
    {
        var result = GetClient(relays);

        await result.Item1.ConnectAndWaitUntilConnected(token, CancellationToken.None);

        return result;
    }
    
    private string GetConnString(Uri[] relays)
    {
        return string.Join(';', relays.Select(r => r.ToString()));
    }

    public void KillClient(Uri[] relays)
    {
        var connstring = GetConnString(relays);
        if (_clientPool.TryRemove(connstring, out var clientWrapper))
        {
            clientWrapper.Dispose();
        }
    }

    public void CleanupExpiredClients(object? state)
    {
        foreach (var key in _clientPool.Keys)
        {
            if (_clientPool[key].IsExpired())
            {
                if (_clientPool.TryRemove(key, out var clientWrapper))
                {
                    clientWrapper.Dispose();
                }
            }
        }
    }

    private class UsageDisposable : IDisposable
    {
        private readonly NostrClientWrapper _clientWrapper;

        public UsageDisposable(NostrClientWrapper clientWrapper)
        {
            _clientWrapper = clientWrapper;
        }

        public void Dispose()
        {
            _clientWrapper.DecrementUsage();
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
        foreach (var client in _clientPool.Values)
        {
            client.Dispose();
        }
    }
}