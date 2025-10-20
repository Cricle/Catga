using Catga.Common;
using Catga.Core;

namespace Catga.Outbox;

/// <summary>In-memory outbox store (AOT compatible)</summary>
public class MemoryOutboxStore : BaseMemoryStore<OutboxMessage>, IOutboxStore
{
    public ValueTask AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ValidationHelper.ValidateNotNull(message);
        ValidationHelper.ValidateMessageId(message.MessageId);
        AddOrUpdateMessage(message.MessageId, message);
        return default;
    }

    public ValueTask<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int maxCount = 100, CancellationToken cancellationToken = default)
        => new(GetMessagesByPredicate(
            message => message.Status == OutboxStatus.Pending && message.RetryCount < message.MaxRetries,
            maxCount,
            Comparer<OutboxMessage>.Create((a, b) => a.CreatedAt.CompareTo(b.CreatedAt))));

    public ValueTask MarkAsPublishedAsync(long messageId, CancellationToken cancellationToken = default)
    {
        if (TryGetMessage(messageId, out var message) && message != null)
        {
            message.Status = OutboxStatus.Published;
            message.PublishedAt = DateTime.UtcNow;
        }
        return default;
    }

    public ValueTask MarkAsFailedAsync(long messageId, string errorMessage, CancellationToken cancellationToken = default)
    {
        if (TryGetMessage(messageId, out var message) && message != null)
        {
            message.RetryCount++;
            message.LastError = errorMessage;
            message.Status = message.RetryCount >= message.MaxRetries ? OutboxStatus.Failed : OutboxStatus.Pending;
        }
        return default;
    }

    public ValueTask DeletePublishedMessagesAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default)
        => DeleteExpiredMessagesAsync(
            retentionPeriod,
            m => m.PublishedAt,
            m => m.Status == OutboxStatus.Published,
            cancellationToken);

    public int GetMessageCountByStatus(OutboxStatus status) => GetCountByPredicate(m => m.Status == status);
}

