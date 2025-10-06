using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Catga.Inbox;
using Catga.Messages;
using Catga.Results;
using Catga.Serialization;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Inbox 行为 - 专注于存储层，确保幂等性
/// 
/// 架构说明：
/// - IInboxStore: 负责持久化存储（可用 Redis, SQL, MongoDB 等）
/// - 不涉及传输层（传输由 IMessageTransport 负责）
/// - 专注于消息去重和幂等性保证
/// 
/// 流程：
/// 1. 尝试锁定消息（如果已处理，直接返回缓存结果）
/// 2. 执行业务逻辑
/// 3. 保存处理结果到 Inbox
/// 4. 释放锁定
/// </summary>
/// <remarks>
/// 参考 MassTransit 的设计：Inbox 专注于幂等性，不涉及传输
/// </remarks>
[RequiresUnreferencedCode("Inbox 行为需要序列化支持。生产环境请使用 AOT 友好的序列化器（如 MemoryPack）")]
[RequiresDynamicCode("Inbox 行为需要序列化支持。生产环境请使用 AOT 友好的序列化器（如 MemoryPack）")]
public class InboxBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : class, IRequest<TResponse>
{
    private readonly IInboxStore? _persistence;
    private readonly IMessageSerializer? _serializer;
    private readonly ILogger<InboxBehavior<TRequest, TResponse>> _logger;
    private readonly TimeSpan _lockDuration;

    public InboxBehavior(
        ILogger<InboxBehavior<TRequest, TResponse>> logger,
        IInboxStore? persistence = null,
        IMessageSerializer? serializer = null,
        TimeSpan? lockDuration = null)
    {
        _logger = logger;
        _persistence = persistence;
        _serializer = serializer;
        _lockDuration = lockDuration ?? TimeSpan.FromMinutes(5);
    }

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        // 如果没有配置 inbox，直接执行
        if (_persistence == null)
            return await next();

        // 获取 MessageId
        string? messageId = null;
        if (request is IMessage message && !string.IsNullOrEmpty(message.MessageId))
        {
            messageId = message.MessageId;
        }

        // 如果没有 MessageId，无法使用 inbox 模式，直接执行
        if (string.IsNullOrEmpty(messageId))
        {
            _logger.LogDebug("No MessageId found for {RequestType}, skipping inbox check", typeof(TRequest).Name);
            return await next();
        }

        try
        {
            // 检查消息是否已经处理过
            var hasBeenProcessed = await _persistence.HasBeenProcessedAsync(messageId, cancellationToken);
            if (hasBeenProcessed)
            {
                _logger.LogInformation("Message {MessageId} has already been processed, returning cached result", messageId);

                // 获取缓存的结果
                var cachedResult = await _persistence.GetProcessedResultAsync(messageId, cancellationToken);
                if (!string.IsNullOrEmpty(cachedResult))
                {
                    try
                    {
                        var result = DeserializeResult(cachedResult);
                        if (result != null)
                            return result;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to deserialize cached result for message {MessageId}", messageId);
                    }
                }

                // 如果无法反序列化缓存结果，返回成功但无值
                return CatgaResult<TResponse>.Success(default!);
            }

            // 尝试锁定消息
            var lockAcquired = await _persistence.TryLockMessageAsync(messageId, _lockDuration, cancellationToken);
            if (!lockAcquired)
            {
                _logger.LogWarning("Failed to acquire lock for message {MessageId}, another instance may be processing it", messageId);
                return CatgaResult<TResponse>.Failure("Message is being processed by another instance");
            }

            try
            {
                // 执行业务逻辑
                var result = await next();

                // 保存处理结果到 inbox
                var inboxMessage = new InboxMessage
                {
                    MessageId = messageId,
                    MessageType = typeof(TRequest).AssemblyQualifiedName ?? typeof(TRequest).FullName ?? typeof(TRequest).Name,
                    Payload = SerializeRequest(request),
                    ProcessingResult = SerializeResult(result),
                    CorrelationId = (request as IMessage)?.CorrelationId
                };

                await _persistence.MarkAsProcessedAsync(inboxMessage, cancellationToken);

                _logger.LogDebug("Marked message {MessageId} as processed in inbox", messageId);

                return result;
            }
            catch (Exception)
            {
                // 处理失败，释放锁
                await _persistence.ReleaseLockAsync(messageId, cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in inbox behavior for message {MessageId}", messageId);
            throw;
        }
    }

    private string SerializeRequest(TRequest request)
    {
        if (_serializer != null)
        {
            var bytes = _serializer.Serialize(request);
            return Convert.ToBase64String(bytes);
        }
        return JsonSerializer.Serialize(request);
    }

    private string SerializeResult(CatgaResult<TResponse> result)
    {
        if (_serializer != null)
        {
            var bytes = _serializer.Serialize(result);
            return Convert.ToBase64String(bytes);
        }
        return JsonSerializer.Serialize(result);
    }

    private CatgaResult<TResponse>? DeserializeResult(string json)
    {
        if (_serializer != null)
        {
            var bytes = Convert.FromBase64String(json);
            return _serializer.Deserialize<CatgaResult<TResponse>>(bytes);
        }
        return JsonSerializer.Deserialize<CatgaResult<TResponse>>(json);
    }
}

