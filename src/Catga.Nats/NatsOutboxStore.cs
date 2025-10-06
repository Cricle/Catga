using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Catga.Outbox;
using Catga.Serialization;

namespace Catga.Nats;

/// <summary>
/// NATS Outbox 存储实现（基于内存 + 序列化抽象）
/// 注意：生产环境建议使用 Redis 实现持久化
/// </summary>
public class NatsOutboxStore : IOutboxStore
{
    private readonly ConcurrentDictionary<string, byte[]> _messages = new();
    private readonly IMessageSerializer _serializer;

    public NatsOutboxStore(IMessageSerializer serializer)
    {
        _serializer = serializer;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
    public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        if (string.IsNullOrEmpty(message.MessageId)) throw new ArgumentException("MessageId is required");

        var data = _serializer.Serialize(message);
        _messages[message.MessageId] = data;

        return Task.CompletedTask;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
    public Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default)
    {
        var pending = new List<OutboxMessage>();

        foreach (var kvp in _messages)
        {
            var message = _serializer.Deserialize<OutboxMessage>(kvp.Value);
            if (message != null &&
                message.Status == OutboxStatus.Pending &&
                message.RetryCount < message.MaxRetries)
            {
                pending.Add(message);

                if (pending.Count >= maxCount)
                    break;
            }
        }

        // 按创建时间排序
        pending.Sort((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));
        return Task.FromResult<IReadOnlyList<OutboxMessage>>(pending);
    }

    public Task MarkAsPublishedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (_messages.TryGetValue(messageId, out var data))
        {
            var message = _serializer.Deserialize<OutboxMessage>(data);
            if (message != null)
            {
                message.Status = OutboxStatus.Published;
                message.PublishedAt = DateTime.UtcNow;

                _messages[messageId] = _serializer.Serialize(message);
            }
        }

        return Task.CompletedTask;
    }

    public Task MarkAsFailedAsync(
        string messageId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        if (_messages.TryGetValue(messageId, out var data))
        {
            var message = _serializer.Deserialize<OutboxMessage>(data);
            if (message != null)
            {
                message.RetryCount++;
                message.LastError = errorMessage;

                if (message.RetryCount >= message.MaxRetries)
                {
                    message.Status = OutboxStatus.Failed;
                }
                else
                {
                    message.Status = OutboxStatus.Pending;
                }

                _messages[messageId] = _serializer.Serialize(message);
            }
        }

        return Task.CompletedTask;
    }

    public Task DeletePublishedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - retentionPeriod;
        var keysToDelete = new List<string>();

        foreach (var kvp in _messages)
        {
            var message = _serializer.Deserialize<OutboxMessage>(kvp.Value);
            if (message != null &&
                message.Status == OutboxStatus.Published &&
                message.PublishedAt.HasValue &&
                message.PublishedAt.Value < cutoff)
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
