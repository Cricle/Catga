using Catga.Common;

namespace Catga.Outbox;

/// <summary>In-memory outbox store (AOT compatible)</summary>
public class MemoryOutboxStore : BaseMemoryStore<OutboxMessage>, IOutboxStore
{
    public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        MessageHelper.ValidateMessageId(message.MessageId, nameof(message.MessageId));
        AddOrUpdateMessage(message.MessageId, message);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int maxCount = 100, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<OutboxMessage>>(
            GetMessagesByPredicate(
                message => message.Status == OutboxStatus.Pending && message.RetryCount < message.MaxRetries,
                maxCount,
                Comparer<OutboxMessage>.Create((a, b) => a.CreatedAt.CompareTo(b.CreatedAt))));

    public Task MarkAsPublishedAsync(string messageId, CancellationToken cancellationToken = default)
        => ExecuteIfExistsAsync(messageId, message =>
        {
            message.Status = OutboxStatus.Published;
            message.PublishedAt = DateTime.UtcNow;
        });

    public Task MarkAsFailedAsync(string messageId, string errorMessage, CancellationToken cancellationToken = default)
        => ExecuteIfExistsAsync(messageId, message =>
        {
            message.RetryCount++;
            message.LastError = errorMessage;
            message.Status = message.RetryCount >= message.MaxRetries ? OutboxStatus.Failed : OutboxStatus.Pending;
        });

    public Task DeletePublishedMessagesAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - retentionPeriod;
        return DeleteExpiredMessagesAsync(
            retentionPeriod,
            message => message.Status == OutboxStatus.Published && message.PublishedAt.HasValue && message.PublishedAt.Value < cutoff,
            cancellationToken);
    }

    public int GetMessageCountByStatus(OutboxStatus status) => GetCountByPredicate(m => m.Status == status);
}

