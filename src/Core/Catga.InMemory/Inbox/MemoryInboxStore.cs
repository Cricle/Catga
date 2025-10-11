using Catga.Common;

namespace Catga.Inbox;

/// <summary>
/// In-memory inbox store implementation (100% AOT compatible)
/// Suitable for development and testing
/// </summary>
public class MemoryInboxStore : BaseMemoryStore<InboxMessage>, IInboxStore
{

    /// <inheritdoc/>
    public Task<bool> TryLockMessageAsync(
        string messageId,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
    {
        MessageHelper.ValidateMessageId(messageId, nameof(messageId));

        // If message already exists
        if (TryGetMessage(messageId, out var existingMessage))
        {
            // Already processed, cannot lock again
            if (existingMessage.Status == InboxStatus.Processed)
                return Task.FromResult(false);

            // Check if lock has expired
            if (existingMessage.LockExpiresAt.HasValue &&
                existingMessage.LockExpiresAt.Value > DateTime.UtcNow)
            {
                // Lock hasn't expired, cannot acquire
                return Task.FromResult(false);
            }

            // Lock expired or no lock, can relock
            existingMessage.Status = InboxStatus.Processing;
            existingMessage.LockExpiresAt = DateTime.UtcNow.Add(lockDuration);
            return Task.FromResult(true);
        }

        // First lock, create new inbox message record
        var newMessage = new InboxMessage
        {
            MessageId = messageId,
            MessageType = string.Empty, // Will be filled during actual processing
            Payload = string.Empty,     // Will be filled during actual processing
            Status = InboxStatus.Processing,
            LockExpiresAt = DateTime.UtcNow.Add(lockDuration)
        };

        AddOrUpdateMessage(messageId, newMessage);
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task MarkAsProcessedAsync(
        InboxMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Update or create message record
        if (TryGetMessage(message.MessageId, out var existing))
        {
            // Update existing record
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
            // Create new record
            message.ProcessedAt = DateTime.UtcNow;
            message.Status = InboxStatus.Processed;
            message.LockExpiresAt = null;
            AddOrUpdateMessage(message.MessageId, message);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> HasBeenProcessedAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (TryGetMessage(messageId, out var message))
        {
            return Task.FromResult(message.Status == InboxStatus.Processed);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public Task<string?> GetProcessedResultAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (TryGetMessage(messageId, out var message) &&
            message.Status == InboxStatus.Processed)
        {
            return Task.FromResult(message.ProcessingResult);
        }

        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc/>
    public Task ReleaseLockAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (TryGetMessage(messageId, out var message))
        {
            message.Status = InboxStatus.Pending;
            message.LockExpiresAt = null;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteProcessedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - retentionPeriod;
        return DeleteExpiredMessagesAsync(
            retentionPeriod,
            message => message.Status == InboxStatus.Processed &&
                      message.ProcessedAt.HasValue &&
                      message.ProcessedAt.Value < cutoff,
            cancellationToken);
    }

    /// <summary>
    /// Get message count by status (for testing/monitoring)
    /// </summary>
    public int GetMessageCountByStatus(InboxStatus status) =>
        GetCountByPredicate(m => m.Status == status);
}

