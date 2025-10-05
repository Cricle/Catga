using System.Collections.Concurrent;
using System.Text.Json;
using Catga.Messages;
using Microsoft.Extensions.Logging;

namespace Catga.DeadLetter;

/// <summary>
/// 精简内存死信队列（无锁，AOT 兼容）
/// </summary>
public class InMemoryDeadLetterQueue : IDeadLetterQueue
{
    private readonly ConcurrentQueue<DeadLetterMessage> _deadLetters = new();
    private readonly ILogger<InMemoryDeadLetterQueue> _logger;
    private readonly int _maxSize;

    public InMemoryDeadLetterQueue(ILogger<InMemoryDeadLetterQueue> logger, int maxSize = 1000)
    {
        _logger = logger;
        _maxSize = maxSize;
    }

    public Task SendAsync<TMessage>(
        TMessage message,
        Exception exception,
        int retryCount,
        CancellationToken cancellationToken = default)
        where TMessage : IMessage
    {
        var deadLetter = new DeadLetterMessage
        {
            MessageId = message.MessageId,
            MessageType = typeof(TMessage).Name,
            MessageJson = JsonSerializer.Serialize(message),
            ExceptionType = exception.GetType().Name,
            ExceptionMessage = exception.Message,
            StackTrace = exception.StackTrace ?? string.Empty,
            RetryCount = retryCount,
            FailedAt = DateTime.UtcNow
        };

        _deadLetters.Enqueue(deadLetter);

        // 简单修剪策略
        while (_deadLetters.Count > _maxSize)
            _deadLetters.TryDequeue(out _);

        _logger.LogWarning("消息已发送到死信队列: {MessageType} {MessageId}, 重试: {RetryCount}",
            deadLetter.MessageType, deadLetter.MessageId, retryCount);

        return Task.CompletedTask;
    }

    public Task<List<DeadLetterMessage>> GetFailedMessagesAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default)
    {
        // 零分配优化：避免 LINQ，直接构建列表
        var result = new List<DeadLetterMessage>(Math.Min(maxCount, _deadLetters.Count));
        var count = 0;

        foreach (var item in _deadLetters)
        {
            if (count >= maxCount) break;
            result.Add(item);
            count++;
        }

        return Task.FromResult(result);
    }
}

