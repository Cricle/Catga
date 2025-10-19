using Catga.Common;
using Catga.Core;

namespace Catga.Outbox;

/// <summary>In-memory outbox store (AOT compatible)</summary>
public class MemoryOutboxStore : BaseMemoryStore<OutboxMessage>, IOutboxStore
{
    public ValueTask AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        MessageHelper.ValidateMessageId(message.MessageId, nameof(message.MessageId));
        AddOrUpdateMessage(message.MessageId, message);
        return default;
    }

    public ValueTask<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int maxCount = 100, CancellationToken cancellationToken = default)
        => new(GetMessagesByPredicate(
            message => message.Status == OutboxStatus.Pending && message.RetryCount < message.MaxRetries,
            maxCount,
            Comparer<OutboxMessage>.Create((a, b) => a.CreatedAt.CompareTo(b.CreatedAt))));

    public ValueTask MarkAsPublishedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (TryGetMessage(messageId, out var message) && message != null)
        {
            message.Status = OutboxStatus.Published;
            message.PublishedAt = DateTime.UtcNow;
        }
        return default;
    }

    public ValueTask MarkAsFailedAsync(string messageId, string errorMessage, CancellationToken cancellationToken = default)
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
    {
        var cutoff = DateTime.UtcNow - retentionPeriod;
        var keysToRemove = Messages.Where(kvp => kvp.Value.Status == OutboxStatus.Published && kvp.Value.PublishedAt.HasValue && kvp.Value.PublishedAt.Value < cutoff).Select(kvp => kvp.Key).ToList();
        foreach (var key in keysToRemove)
            Messages.TryRemove(key, out _);
        return default;
    }

    public int GetMessageCountByStatus(OutboxStatus status) => GetCountByPredicate(m => m.Status == status);
}

