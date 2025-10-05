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

        // 如果消息已存在
        if (_messages.TryGetValue(messageId, out var existingMessage))
        {
            // 已经处理完成，不能再锁定
            if (existingMessage.Status == InboxStatus.Processed)
                return Task.FromResult(false);

            // 检查锁是否已过期
            if (existingMessage.LockExpiresAt.HasValue &&
                existingMessage.LockExpiresAt.Value > DateTime.UtcNow)
            {
                // 锁还未过期，不能获取
                return Task.FromResult(false);
            }

            // 锁已过期或没有锁，可以重新锁定
            existingMessage.Status = InboxStatus.Processing;
            existingMessage.LockExpiresAt = DateTime.UtcNow.Add(lockDuration);
            return Task.FromResult(true);
        }

        // 首次锁定，创建新的 inbox 消息记录
        var newMessage = new InboxMessage
        {
            MessageId = messageId,
            MessageType = string.Empty, // 将在实际处理时填充
            Payload = string.Empty,     // 将在实际处理时填充
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

