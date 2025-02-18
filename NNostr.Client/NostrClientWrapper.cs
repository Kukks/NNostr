﻿namespace NNostr.Client;

public class NostrClientWrapper : IDisposable
{
    public INostrClient Client { get; private set; }
    internal int UsageCount = 0;
    private bool _isDisposed = false;
    private DateTimeOffset _lastUsed;

    public NostrClientWrapper(INostrClient client)
    {
        Client = client;
        _lastUsed = DateTimeOffset.UtcNow;
    }

    public void IncrementUsage()
    {
        _lastUsed = DateTimeOffset.UtcNow;
        Interlocked.Increment(ref UsageCount);
    }

    public void DecrementUsage()
    {
        _lastUsed = DateTimeOffset.UtcNow;
        if (Interlocked.Decrement(ref UsageCount) == 0 && IsExpired())
        {
            Dispose();
        }
    }

    public bool IsExpired()
    {
        return DateTimeOffset.UtcNow - _lastUsed > TimeSpan.FromMinutes(5);
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            Client.Dispose();
            _isDisposed = true;
        }
    }
}