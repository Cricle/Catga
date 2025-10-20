using System.Collections.Concurrent;

namespace Catga.Idempotency;

/// <summary>In-memory idempotency store for QoS 2 (Exactly Once)</summary>
internal sealed class InMemoryIdempotencyStore
{
    private readonly ConcurrentDictionary<long, DateTime> _processedMessages = new();
    private readonly TimeSpan _retentionPeriod;

    public InMemoryIdempotencyStore(TimeSpan? retentionPeriod = null)
        => _retentionPeriod = retentionPeriod ?? TimeSpan.FromHours(24);

    public bool IsProcessed(long messageId)
    {
        CleanupExpired();
        return _processedMessages.ContainsKey(messageId);
    }

    public void MarkAsProcessed(long messageId) => _processedMessages.TryAdd(messageId, DateTime.UtcNow);

    private void CleanupExpired()
    {
        var cutoff = DateTime.UtcNow - _retentionPeriod;

        // Manual enumeration instead of LINQ Where/Select
        // ConcurrentDictionary supports modification during enumeration
        foreach (var kvp in _processedMessages)
        {
            if (kvp.Value < cutoff)
                _processedMessages.TryRemove(kvp.Key, out _);
        }
    }
}

