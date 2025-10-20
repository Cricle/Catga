using Catga.Common;
using Catga.Core;

namespace Catga.Inbox;

/// <summary>In-memory inbox store (AOT compatible)</summary>
public class MemoryInboxStore : BaseMemoryStore<InboxMessage>, IInboxStore
{
    public ValueTask<bool> TryLockMessageAsync(long messageId, TimeSpan lockDuration, CancellationToken cancellationToken = default)
    {
        ValidationHelper.ValidateMessageId(messageId);

        if (TryGetMessage(messageId, out var existingMessage) && existingMessage != null)
        {
            if (existingMessage.Status == InboxStatus.Processed) return new(false);
            if (existingMessage.LockExpiresAt.HasValue && existingMessage.LockExpiresAt.Value > DateTime.UtcNow) return new(false);

            existingMessage.Status = InboxStatus.Processing;
            existingMessage.LockExpiresAt = DateTime.UtcNow.Add(lockDuration);
            return new(true);
        }

        var newMessage = new InboxMessage
        {
            MessageId = messageId,
            MessageType = string.Empty,
            Payload = string.Empty,
            Status = InboxStatus.Processing,
            LockExpiresAt = DateTime.UtcNow.Add(lockDuration)
        };

        AddOrUpdateMessage(messageId, newMessage);
        return new(true);
    }

    public ValueTask MarkAsProcessedAsync(InboxMessage message, CancellationToken cancellationToken = default)
    {
        ValidationHelper.ValidateNotNull(message);

        if (TryGetMessage(message.MessageId, out var existing) && existing != null)
        {
            existing.MessageType = message.MessageType;
            existing.Payload = message.Payload;
            existing.ProcessedAt = DateTime.UtcNow;
            existing.ProcessingResult = message.ProcessingResult;
            existing.Status = InboxStatus.Processed;
            existing.LockExpiresAt = null;
            existing.CorrelationId = message.CorrelationId;
            existing.Metadata = message.Metadata;
        }
        else
        {
            message.ProcessedAt = DateTime.UtcNow;
            message.Status = InboxStatus.Processed;
            message.LockExpiresAt = null;
            AddOrUpdateMessage(message.MessageId, message);
        }

        return default;
    }

    public ValueTask<bool> HasBeenProcessedAsync(long messageId, CancellationToken cancellationToken = default)
        => new(TryGetMessage(messageId, out var message) && message != null && message.Status == InboxStatus.Processed);

    public ValueTask<string?> GetProcessedResultAsync(long messageId, CancellationToken cancellationToken = default)
        => new(TryGetMessage(messageId, out var message) && message != null && message.Status == InboxStatus.Processed ? message.ProcessingResult : null);

    public ValueTask ReleaseLockAsync(long messageId, CancellationToken cancellationToken = default)
    {
        if (TryGetMessage(messageId, out var message) && message != null)
        {
            message.Status = InboxStatus.Pending;
            message.LockExpiresAt = null;
        }
        return default;
    }

    public ValueTask DeleteProcessedMessagesAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default)
        => DeleteExpiredMessagesAsync(
            retentionPeriod,
            m => m.ProcessedAt,
            m => m.Status == InboxStatus.Processed,
            cancellationToken);

    public int GetMessageCountByStatus(InboxStatus status) => GetCountByPredicate(m => m.Status == status);
}

