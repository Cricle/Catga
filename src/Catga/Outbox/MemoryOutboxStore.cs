using System.Collections.Concurrent;
using Catga.Common;

namespace Catga.Outbox;

/// <summary>
/// In-memory outbox store implementation (100% AOT compatible)
/// Suitable for development and testing
/// </summary>
public class MemoryOutboxStore : IOutboxStore
{
    private readonly ConcurrentDictionary<string, OutboxMessage> _messages = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <inheritdoc/>
    public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        MessageHelper.ValidateMessageId(message.MessageId, nameof(message.MessageId));

        _messages[message.MessageId] = message;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default)
    {
        var pending = new List<OutboxMessage>(maxCount);

        // Zero-allocation iteration: direct iteration, avoid LINQ
        foreach (var kvp in _messages)
        {
            var message = kvp.Value;

            // Only get Pending messages that haven't exceeded retry count
            if (message.Status == OutboxStatus.Pending &&
                message.RetryCount < message.MaxRetries)
            {
                pending.Add(message);

                if (pending.Count >= maxCount)
                    break;
            }
        }

        // Sort by creation time (FIFO)
        if (pending.Count > 1)
        {
            pending.Sort((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));
        }

        return Task.FromResult<IReadOnlyList<OutboxMessage>>(pending);
    }

    /// <inheritdoc/>
    public Task MarkAsPublishedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (_messages.TryGetValue(messageId, out var message))
        {
            message.Status = OutboxStatus.Published;
            message.PublishedAt = DateTime.UtcNow;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task MarkAsFailedAsync(
        string messageId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        if (_messages.TryGetValue(messageId, out var message))
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
    public Task DeletePublishedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - retentionPeriod;
        return MessageStoreHelper.DeleteExpiredMessagesAsync(
            _messages,
            _lock,
            retentionPeriod,
            message => message.Status == OutboxStatus.Published &&
                      message.PublishedAt.HasValue &&
                      message.PublishedAt.Value < cutoff,
            cancellationToken);
    }

    /// <summary>
    /// Get total message count (for testing/monitoring)
    /// </summary>
    public int GetMessageCount() => _messages.Count;

    /// <summary>
    /// Get message count by status (for testing/monitoring)
    /// </summary>
    public int GetMessageCountByStatus(OutboxStatus status) =>
        MessageStoreHelper.GetMessageCountByPredicate(_messages, m => m.Status == status);
}

