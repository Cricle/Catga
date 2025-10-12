using System.Collections.Concurrent;
using Catga.Common;

namespace Catga.Idempotency;

/// <summary>In-memory idempotency store for QoS 2 (Exactly Once)</summary>
internal sealed class InMemoryIdempotencyStore
{
    private readonly ConcurrentDictionary<string, DateTime> _processedMessages = new();
    private readonly TimeSpan _retentionPeriod;

    public InMemoryIdempotencyStore(TimeSpan? retentionPeriod = null)
        => _retentionPeriod = retentionPeriod ?? TimeSpan.FromHours(24);

    public bool IsProcessed(string messageId)
    {
        CleanupExpired();
        return _processedMessages.ContainsKey(messageId);
    }

    public void MarkAsProcessed(string messageId) => _processedMessages.TryAdd(messageId, DateTime.UtcNow);

    private void CleanupExpired()
        => ExpirationHelper.CleanupExpired(_processedMessages, timestamp => timestamp, _retentionPeriod);
}

