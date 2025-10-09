using System.Diagnostics.CodeAnalysis;
using Catga.Common;

namespace Catga.Outbox;

/// <summary>
/// In-memory outbox store implementation (100% AOT compatible)
/// Suitable for development and testing
/// </summary>
public class MemoryOutboxStore : BaseMemoryStore<OutboxMessage>, IOutboxStore
{

    /// <inheritdoc/>
    [RequiresDynamicCode("JSON serialization may require dynamic code generation")]
    [RequiresUnreferencedCode("JSON serialization may require unreferenced code")]
    public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        MessageHelper.ValidateMessageId(message.MessageId, nameof(message.MessageId));

        AddOrUpdateMessage(message.MessageId, message);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    [RequiresDynamicCode("JSON deserialization may require dynamic code generation")]
    [RequiresUnreferencedCode("JSON deserialization may require unreferenced code")]
    public Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default)
    {
        var comparer = Comparer<OutboxMessage>.Create((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));
        var pending = GetMessagesByPredicate(
            message => message.Status == OutboxStatus.Pending && message.RetryCount < message.MaxRetries,
            maxCount,
            comparer);

        return Task.FromResult<IReadOnlyList<OutboxMessage>>(pending);
    }

    /// <inheritdoc/>
    [RequiresDynamicCode("JSON serialization may require dynamic code generation")]
    [RequiresUnreferencedCode("JSON serialization may require unreferenced code")]
    public Task MarkAsPublishedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (TryGetMessage(messageId, out var message))
        {
            message.Status = OutboxStatus.Published;
            message.PublishedAt = DateTime.UtcNow;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    [RequiresDynamicCode("JSON serialization may require dynamic code generation")]
    [RequiresUnreferencedCode("JSON serialization may require unreferenced code")]
    public Task MarkAsFailedAsync(
        string messageId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        if (TryGetMessage(messageId, out var message))
        {
            message.RetryCount++;
            message.LastError = errorMessage;

            // If max retries exceeded, mark as failed
            if (message.RetryCount >= message.MaxRetries)
            {
                message.Status = OutboxStatus.Failed;
            }
            else
            {
                message.Status = OutboxStatus.Pending;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    [RequiresDynamicCode("JSON deserialization may require dynamic code generation")]
    [RequiresUnreferencedCode("JSON deserialization may require unreferenced code")]
    public Task DeletePublishedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - retentionPeriod;
        return DeleteExpiredMessagesAsync(
            retentionPeriod,
            message => message.Status == OutboxStatus.Published &&
                      message.PublishedAt.HasValue &&
                      message.PublishedAt.Value < cutoff,
            cancellationToken);
    }

    /// <summary>
    /// Get message count by status (for testing/monitoring)
    /// </summary>
    public int GetMessageCountByStatus(OutboxStatus status) =>
        GetCountByPredicate(m => m.Status == status);
}

