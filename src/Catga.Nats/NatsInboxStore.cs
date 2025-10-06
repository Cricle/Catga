using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Catga.Inbox;
using Catga.Serialization;

namespace Catga.Nats;

/// <summary>
/// NATS Inbox 存储实现（基于内存 + 序列化抽象）
/// 注意：生产环境建议使用 Redis 实现持久化和分布式锁
/// </summary>
public class NatsInboxStore : IInboxStore
{
    private readonly ConcurrentDictionary<string, byte[]> _messages = new();
    private readonly IMessageSerializer _serializer;

    public NatsInboxStore(IMessageSerializer serializer)
    {
        _serializer = serializer;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
    public Task<bool> TryLockMessageAsync(
        string messageId,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(messageId))
            throw new ArgumentException("MessageId is required", nameof(messageId));

        InboxMessage? existingMessage = null;
        if (_messages.TryGetValue(messageId, out var data))
        {
            existingMessage = _serializer.Deserialize<InboxMessage>(data);
        }

        if (existingMessage != null)
        {
            // 已经处理完成，不能再锁定
            if (existingMessage.Status == InboxStatus.Processed)
                return Task.FromResult(false);

            // 检查锁是否已过期
            if (existingMessage.LockExpiresAt.HasValue &&
                existingMessage.LockExpiresAt.Value > DateTime.UtcNow)
            {
                return Task.FromResult(false); // 锁还未过期
            }

            // 锁已过期或没有锁，可以重新锁定
            existingMessage.Status = InboxStatus.Processing;
            existingMessage.LockExpiresAt = DateTime.UtcNow.Add(lockDuration);

            _messages[messageId] = _serializer.Serialize(existingMessage);
            return Task.FromResult(true);
        }

        // 首次锁定
        var newMessage = new InboxMessage
        {
            MessageId = messageId,
            MessageType = string.Empty,
            Payload = string.Empty,
            Status = InboxStatus.Processing,
            LockExpiresAt = DateTime.UtcNow.Add(lockDuration)
        };

        _messages[messageId] = _serializer.Serialize(newMessage);
        return Task.FromResult(true);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
    public Task MarkAsProcessedAsync(
        InboxMessage message,
        CancellationToken cancellationToken = default)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));

        InboxMessage? existing = null;
        if (_messages.TryGetValue(message.MessageId, out var data))
        {
            existing = _serializer.Deserialize<InboxMessage>(data);
        }

        if (existing != null)
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

            _messages[message.MessageId] = _serializer.Serialize(existing);
        }
        else
        {
            // 创建新记录
            message.ProcessedAt = DateTime.UtcNow;
            message.Status = InboxStatus.Processed;
            message.LockExpiresAt = null;

            _messages[message.MessageId] = _serializer.Serialize(message);
        }

        return Task.CompletedTask;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
    public Task<bool> HasBeenProcessedAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (_messages.TryGetValue(messageId, out var data))
        {
            var message = _serializer.Deserialize<InboxMessage>(data);
            return Task.FromResult(message?.Status == InboxStatus.Processed);
        }

        return Task.FromResult(false);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
    public Task<string?> GetProcessedResultAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (_messages.TryGetValue(messageId, out var data))
        {
            var message = _serializer.Deserialize<InboxMessage>(data);
            return Task.FromResult(message?.ProcessingResult);
        }

        return Task.FromResult<string?>(null);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
    public Task ReleaseLockAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (_messages.TryGetValue(messageId, out var data))
        {
            var message = _serializer.Deserialize<InboxMessage>(data);
            if (message != null && message.Status != InboxStatus.Processed)
            {
                message.LockExpiresAt = null;
                message.Status = InboxStatus.Pending;

                _messages[messageId] = _serializer.Serialize(message);
            }
        }

        return Task.CompletedTask;
    }

    public Task DeleteProcessedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - retentionPeriod;
        var keysToDelete = new List<string>();

        foreach (var kvp in _messages)
        {
            var message = _serializer.Deserialize<InboxMessage>(kvp.Value);
            if (message != null &&
                message.Status == InboxStatus.Processed &&
                message.ProcessedAt.HasValue &&
                message.ProcessedAt.Value < cutoff)
            {
                keysToDelete.Add(kvp.Key);
            }
        }

        foreach (var key in keysToDelete)
        {
            _messages.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }
}
