using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Catga.Idempotency;
using Catga.Serialization;

namespace Catga.Nats;

/// <summary>
/// NATS 幂等性存储实现（基于内存 + 序列化抽象）
/// 注意：生产环境建议使用 Redis 实现持久化
/// </summary>
public class NatsIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, byte[]> _entries = new();
    private readonly IMessageSerializer _serializer;

    public NatsIdempotencyStore(IMessageSerializer serializer)
    {
        _serializer = serializer;
    }

    public Task<bool> HasBeenProcessedAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_entries.ContainsKey(messageId));
    }

    [RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed.")]
    [RequiresDynamicCode("JSON serialization may require dynamic code generation.")]
    public Task MarkAsProcessedAsync<TResult>(
        string messageId,
        TResult? result = default,
        CancellationToken cancellationToken = default)
    {
        var data = new IdempotencyEntry
        {
            MessageId = messageId,
            ProcessedAt = DateTime.UtcNow,
            ResultType = result != null ? typeof(TResult).FullName : null,
            ResultData = result != null ? _serializer.Serialize(result) : null
        };

        _entries[messageId] = _serializer.Serialize(data);
        return Task.CompletedTask;
    }

    [RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed.")]
    [RequiresDynamicCode("JSON serialization may require dynamic code generation.")]
    public Task<TResult?> GetCachedResultAsync<TResult>(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(messageId, out var entryData))
        {
            var data = _serializer.Deserialize<IdempotencyEntry>(entryData);
            if (data?.ResultData != null && data.ResultType == typeof(TResult).FullName)
            {
                return Task.FromResult(_serializer.Deserialize<TResult>(data.ResultData));
            }
        }

        return Task.FromResult<TResult?>(default);
    }

    private class IdempotencyEntry
    {
        public required string MessageId { get; set; }
        public DateTime ProcessedAt { get; set; }
        public string? ResultType { get; set; }
        public byte[]? ResultData { get; set; }
    }
}
