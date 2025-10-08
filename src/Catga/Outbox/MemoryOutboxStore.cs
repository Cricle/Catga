using System.Collections.Concurrent;

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
        if (message == null) throw new ArgumentNullException(nameof(message));
        if (string.IsNullOrEmpty(message.MessageId)) throw new ArgumentException("MessageId is required");

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
    public async Task DeletePublishedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var cutoff = DateTime.UtcNow - retentionPeriod;
            List<string>? keysToRemove = null;

            // 零分配遍历
            foreach (var kvp in _messages)
            {
                var message = kvp.Value;
                if (message.Status == OutboxStatus.Published &&
                    message.PublishedAt.HasValue &&
                    message.PublishedAt.Value < cutoff)
                {
                    keysToRemove ??= new List<string>();
                    keysToRemove.Add(kvp.Key);
                }
            }

            // 删除过期消息
            if (keysToRemove != null)
            {
                foreach (var key in keysToRemove)
                {
                    _messages.TryRemove(key, out _);
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Get total message count (for testing/monitoring)
    /// </summary>
    public int GetMessageCount() => _messages.Count;

    /// <summary>
    /// Get message count by status (for testing/monitoring)
    /// </summary>
    public int GetMessageCountByStatus(OutboxStatus status)
    {
        int count = 0;
        foreach (var kvp in _messages)
        {
            if (kvp.Value.Status == status)
                count++;
        }
        return count;
    }
}

