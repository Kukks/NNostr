namespace NNostr.Client;

/// <summary>
/// Tracks leases on a pooled <see cref="INostrClient"/> and owns its disposal.
/// </summary>
public class NostrClientWrapper : IDisposable
{
    /// <summary>
    /// The underlying client. It stays usable until the wrapper is disposed.
    /// </summary>
    public INostrClient Client { get; private set; }

    /// <summary>
    /// Number of active leases handed out for this wrapper.
    /// </summary>
    internal int UsageCount = 0;

    private int _isDisposed;
    private int _disposeRequested;
    internal DateTimeOffset _lastUsed;

    /// <summary>
    /// Creates a wrapper around <paramref name="client"/>.
    /// </summary>
    /// <param name="client">The client whose lifetime is managed by this wrapper.</param>
    public NostrClientWrapper(INostrClient client)
    {
        Client = client;
        _lastUsed = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Registers a new lease on the wrapped client and refreshes its last-used timestamp.
    /// </summary>
    public void IncrementUsage()
    {
        _lastUsed = DateTimeOffset.UtcNow;
        Interlocked.Increment(ref UsageCount);
    }

    /// <summary>
    /// Releases a lease. When the last lease is returned, the client is disposed if the
    /// pool has requested disposal (for example via <see cref="NostrClientPool.KillClient"/>).
    /// Otherwise it stays in the pool and is removed by the expiration cleanup once idle.
    /// </summary>
    public void DecrementUsage()
    {
        _lastUsed = DateTimeOffset.UtcNow;
        if (Interlocked.Decrement(ref UsageCount) == 0 &&
            Volatile.Read(ref _disposeRequested) == 1)
        {
            Dispose();
        }
    }

    /// <summary>
    /// Determines whether the wrapper has been idle for more than five minutes with no
    /// active leases. A long-lived consumer that keeps a lease open is never considered
    /// expired, even if it does not call into the wrapper for a while.
    /// </summary>
    /// <returns><c>true</c> when the wrapper can be safely removed from the pool.</returns>
    public bool IsExpired()
    {
        // Only expire when nothing holds a lease: a long-lived consumer (eg. an NWC
        // invoice listener) keeps UsageCount above zero without calling
        // Increment/DecrementUsage again, and must not have its client disposed
        // underneath it by the cleanup timer.
        return UsageCount <= 0 && DateTimeOffset.UtcNow - _lastUsed > TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Requests disposal once the last active lease is released. Call this instead of
    /// <see cref="Dispose"/> when the wrapper has been removed from the pool but callers
    /// may still be using the client.
    /// </summary>
    internal void RequestDispose()
    {
        Interlocked.Exchange(ref _disposeRequested, 1);
        if (Volatile.Read(ref UsageCount) <= 0)
        {
            Dispose();
        }
    }

    /// <summary>
    /// Disposes the wrapped client exactly once, regardless of how many concurrent
    /// callers reach this method.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            Client.Dispose();
        }
    }
}
