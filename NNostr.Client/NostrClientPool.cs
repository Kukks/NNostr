using System.Collections.Concurrent;

namespace NNostr.Client;

/// <summary>
/// Reuses <see cref="INostrClient"/> instances per relay set and disposes them once they
/// are no longer leased or have been idle for too long.
/// </summary>
public class NostrClientPool : IDisposable
{
    private readonly ConcurrentDictionary<string, NostrClientWrapper> _clientPool = new();

    // Serializes acquisition (lookup + lease increment) against removal and
    // disposal (expiration cleanup and KillClient), so a wrapper can never be
    // disposed after a concurrent caller has acquired it.
    private readonly object _poolLock = new();

    private Timer? _cleanupTimer;
    private readonly TimeSpan _unusedClientTimeout;

    /// <summary>
    /// Creates a pool whose background cleanup runs every <paramref name="unusedClientTimeout"/>.
    /// </summary>
    /// <param name="unusedClientTimeout">
    /// Interval between expiration sweeps. Defaults to five minutes when omitted.
    /// </param>
    public NostrClientPool(TimeSpan? unusedClientTimeout = null)
    {
        _unusedClientTimeout = unusedClientTimeout ?? TimeSpan.FromMinutes(5);
        InitBackgroundTask();
    }

    /// <summary>
    /// Starts (or restarts) the background timer that calls <see cref="CleanupExpiredClients"/>.
    /// </summary>
    protected virtual void InitBackgroundTask()
    {
        _cleanupTimer?.Dispose();
        _cleanupTimer = new Timer(CleanupExpiredClients, null, _unusedClientTimeout, _unusedClientTimeout);
    }

    /// <summary>
    /// Creates the wrapper for a relay set. Override this in tests to supply a fake client.
    /// </summary>
    /// <param name="relays">Relays the client should connect to.</param>
    /// <returns>A new wrapper owning the client for those relays.</returns>
    protected virtual NostrClientWrapper CreateClientWrapper(Uri[] relays)
    {
        return new NostrClientWrapper(relays.Length > 1
            ? new CompositeNostrClient(relays)
            : new NostrClient(relays[0]));
    }

    /// <summary>
    /// Gets a client for <paramref name="relays"/> and registers a lease on it. Release the
    /// lease by disposing the returned <see cref="IDisposable"/>.
    /// </summary>
    /// <param name="relays">Relays the client should connect to.</param>
    /// <returns>The pooled client and a disposable that releases its lease.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="relays"/> is empty.</exception>
    public (INostrClient, IDisposable) GetClient(Uri[] relays)
    {
        if (relays.Length == 0)
            throw new ArgumentException("At least one relay is required", nameof(relays));

        var connString = GetConnString(relays);

        NostrClientWrapper clientWrapper;
        lock (_poolLock)
        {
            // Lookup and lease increment must be atomic with respect to
            // CleanupExpiredClients and KillClient: otherwise the timer could
            // observe an expired wrapper, then acquire the lock after our lookup
            // but before our increment, and remove and dispose the very wrapper
            // we are about to return.
            clientWrapper = _clientPool.GetOrAdd(connString, _ => CreateClientWrapper(relays));

            clientWrapper.IncrementUsage();
        }

        return (clientWrapper.Client, new UsageDisposable(clientWrapper));
    }

    /// <summary>
    /// Gets a client for <paramref name="relays"/> and waits until it is connected.
    /// If connecting fails, the lease is released and the client is removed from the pool.
    /// </summary>
    /// <param name="relays">Relays the client should connect to.</param>
    /// <param name="token">Cancellation token for the connection attempt.</param>
    /// <returns>The connected client and a disposable that releases its lease.</returns>
    public async Task<(INostrClient, IDisposable)> GetClientAndConnect(Uri[] relays, CancellationToken token)
    {
        var result = GetClient(relays);
        try
        {
            await result.Item1.ConnectAndWaitUntilConnected(token, CancellationToken.None);
            return result;
        }
        catch (Exception e)
        {
            result.Item2.Dispose();
            KillClient(relays);
            throw;
        }
    }

    private string GetConnString(Uri[] relays)
    {
        return string.Join(';', relays.Select(r => r.ToString()));
    }

    /// <summary>
    /// Removes the client for <paramref name="relays"/> from the pool and disposes it once
    /// the last outstanding lease is released, so callers still using the client are not
    /// left with a disposed instance.
    /// </summary>
    /// <param name="relays">Relays identifying the client to remove.</param>
    public void KillClient(Uri[] relays)
    {
        var connstring = GetConnString(relays);
        lock (_poolLock)
        {
            if (_clientPool.TryRemove(connstring, out var clientWrapper))
            {
                clientWrapper.RequestDispose();
            }
        }
    }

    /// <summary>
    /// Removes and disposes clients that have had no active leases for more than five
    /// minutes. Clients that are currently leased are left untouched.
    /// </summary>
    /// <param name="state">Unused timer state.</param>
    public void CleanupExpiredClients(object? state)
    {
        foreach (var key in _clientPool.Keys)
        {
            lock (_poolLock)
            {
                // Re-check under the lock: the entry may have been acquired or
                // removed since the key was enumerated.
                if (!_clientPool.TryGetValue(key, out var clientWrapper) || !clientWrapper.IsExpired())
                    continue;

                if (_clientPool.TryRemove(key, out clientWrapper))
                {
                    clientWrapper.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Releases a single lease on a pooled client.
    /// </summary>
    internal class UsageDisposable : IDisposable
    {
        internal readonly NostrClientWrapper ClientWrapper;

        /// <summary>
        /// Creates a lease handle for <paramref name="clientWrapper"/>.
        /// </summary>
        /// <param name="clientWrapper">Wrapper whose lease this handle owns.</param>
        public UsageDisposable(NostrClientWrapper clientWrapper)
        {
            ClientWrapper = clientWrapper;
        }

        /// <summary>
        /// Releases the lease, disposing the client if the pool has requested its removal.
        /// </summary>
        public void Dispose()
        {
            ClientWrapper.DecrementUsage();
        }
    }

    /// <summary>
    /// Stops the cleanup timer and disposes every client still held by the pool.
    /// </summary>
    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        lock (_poolLock)
        {
            foreach (var client in _clientPool.Values)
            {
                client.Dispose();
            }
        }
    }
}
