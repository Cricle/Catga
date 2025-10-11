using System.Collections.Concurrent;

namespace Catga.Idempotency;

/// <summary>
/// In-memory idempotency store for QoS 2 (Exactly Once)
/// </summary>
internal sealed class InMemoryIdempotencyStore
{
    private readonly ConcurrentDictionary<string, DateTime> _processedMessages = new();
    private readonly TimeSpan _retentionPeriod;

    public InMemoryIdempotencyStore(TimeSpan? retentionPeriod = null)
    {
        _retentionPeriod = retentionPeriod ?? TimeSpan.FromHours(24);
    }

    /// <summary>
    /// Check if message has already been processed
    /// </summary>
    public bool IsProcessed(string messageId)
    {
        CleanupExpired();
        return _processedMessages.ContainsKey(messageId);
    }

    /// <summary>
    /// Mark message as processed
    /// </summary>
    public void MarkAsProcessed(string messageId)
    {
        _processedMessages.TryAdd(messageId, DateTime.UtcNow);
    }

    /// <summary>
    /// Cleanup expired entries (older than retention period)
    /// </summary>
    private void CleanupExpired()
    {
        var cutoff = DateTime.UtcNow - _retentionPeriod;
        var expiredKeys = _processedMessages
            .Where(kvp => kvp.Value < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _processedMessages.TryRemove(key, out _);
        }
    }
}

