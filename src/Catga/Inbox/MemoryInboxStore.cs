using System.Collections.Concurrent;

namespace Catga.Inbox;

/// <summary>
/// In-memory inbox store implementation (100% AOT compatible)
/// Suitable for development and testing
/// </summary>
public class MemoryInboxStore : IInboxStore
{
    private readonly ConcurrentDictionary<string, InboxMessage> _messages = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <inheritdoc/>
    public Task<bool> TryLockMessageAsync(
        string messageId,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(messageId))
            throw new ArgumentException("MessageId is required", nameof(messageId));

        // If message already exists
        if (_messages.TryGetValue(messageId, out var existingMessage))
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

        _messages[messageId] = newMessage;
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task MarkAsProcessedAsync(
        InboxMessage message,
        CancellationToken cancellationToken = default)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));

        // 更新或创建消息记录
        if (_messages.TryGetValue(message.MessageId, out var existing))
        {
            // 更新现有记录
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
            // 创建新记录
            message.ProcessedAt = DateTime.UtcNow;
            message.Status = InboxStatus.Processed;
            message.LockExpiresAt = null;
            _messages[message.MessageId] = message;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> HasBeenProcessedAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (_messages.TryGetValue(messageId, out var message))
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
        if (_messages.TryGetValue(messageId, out var message) &&
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
        if (_messages.TryGetValue(messageId, out var message))
        {
            message.Status = InboxStatus.Pending;
            message.LockExpiresAt = null;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task DeleteProcessedMessagesAsync(
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
                if (message.Status == InboxStatus.Processed &&
                    message.ProcessedAt.HasValue &&
                    message.ProcessedAt.Value < cutoff)
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
    public int GetMessageCountByStatus(InboxStatus status)
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

