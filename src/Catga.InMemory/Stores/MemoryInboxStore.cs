using Catga.Common;

namespace Catga.Inbox;

/// <summary>In-memory inbox store (AOT compatible)</summary>
public class MemoryInboxStore : BaseMemoryStore<InboxMessage>, IInboxStore
{
    public Task<bool> TryLockMessageAsync(string messageId, TimeSpan lockDuration, CancellationToken cancellationToken = default)
    {
        MessageHelper.ValidateMessageId(messageId, nameof(messageId));

        if (TryGetMessage(messageId, out var existingMessage) && existingMessage != null)
        {
            if (existingMessage.Status == InboxStatus.Processed) return Task.FromResult(false);
            if (existingMessage.LockExpiresAt.HasValue && existingMessage.LockExpiresAt.Value > DateTime.UtcNow) return Task.FromResult(false);

            existingMessage.Status = InboxStatus.Processing;
            existingMessage.LockExpiresAt = DateTime.UtcNow.Add(lockDuration);
            return Task.FromResult(true);
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
        return Task.FromResult(true);
    }

    public Task MarkAsProcessedAsync(InboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

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

        return Task.CompletedTask;
    }

    public Task<bool> HasBeenProcessedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (TryGetMessage(messageId, out var message) && message != null)
            return Task.FromResult(message.Status == InboxStatus.Processed);
        return Task.FromResult(false);
    }

    public Task<string?> GetProcessedResultAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (TryGetMessage(messageId, out var message) && message != null && message.Status == InboxStatus.Processed)
            return Task.FromResult(message.ProcessingResult);
        return Task.FromResult<string?>(null);
    }

    public Task ReleaseLockAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (TryGetMessage(messageId, out var message) && message != null)
        {
            message.Status = InboxStatus.Pending;
            message.LockExpiresAt = null;
        }
        return Task.CompletedTask;
    }

    public Task DeleteProcessedMessagesAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - retentionPeriod;
        return DeleteExpiredMessagesAsync(retentionPeriod, message => message.Status == InboxStatus.Processed && message.ProcessedAt.HasValue && message.ProcessedAt.Value < cutoff, cancellationToken);
    }

    public int GetMessageCountByStatus(InboxStatus status) => GetCountByPredicate(m => m.Status == status);
}

