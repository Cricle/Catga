using System.Diagnostics.CodeAnalysis;
using Catga.Inbox;
using Catga.Messages;
using Catga.Results;
using Catga.Serialization;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Inbox 行为 V2 - 专注于存储层，确保幂等性
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
public class InboxBehaviorV2<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : class, IRequest<TResponse>
{
    private readonly IInboxStore? _persistence;
    private readonly IMessageSerializer? _serializer;
    private readonly ILogger<InboxBehaviorV2<TRequest, TResponse>> _logger;
    private readonly TimeSpan _lockDuration = TimeSpan.FromMinutes(5);

    public InboxBehaviorV2(
        ILogger<InboxBehaviorV2<TRequest, TResponse>> logger,
        IInboxStore? persistence = null,
        IMessageSerializer? serializer = null)
    {
        _logger = logger;
        _persistence = persistence;
        _serializer = serializer;
    }

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        // 如果没有配置 inbox，直接执行
        if (_persistence == null)
            return await next();

        // 只对命令（写操作）使用 inbox
        // 查询不需要幂等性保证
        if (request is IQuery<TResponse>)
            return await next();

        var messageId = GenerateMessageId(request);

        try
        {
            // 1️⃣ 检查是否已处理
            if (await _persistence.HasBeenProcessedAsync(messageId, cancellationToken))
            {
                _logger.LogInformation("[Inbox] Message {MessageId} already processed, returning cached result", messageId);

                // 返回缓存的结果
                var cachedResult = await _persistence.GetProcessedResultAsync(messageId, cancellationToken);
                if (cachedResult != null)
                {
                    var result = DeserializeResult(cachedResult);
                    return CatgaResult<TResponse>.Success(result);
                }

                // 如果没有缓存结果，返回错误
                return CatgaResult<TResponse>.Failure("No cached result found");
            }

            // 2️⃣ 尝试锁定消息（防止并发处理）
            bool locked = await _persistence.TryLockMessageAsync(messageId, _lockDuration, cancellationToken);
            if (!locked)
            {
                _logger.LogWarning("[Inbox] Message {MessageId} is being processed by another instance", messageId);
                return CatgaResult<TResponse>.Failure("Message is being processed");
            }

            _logger.LogDebug("[Inbox] Locked message {MessageId} for processing", messageId);

            try
            {
                // 3️⃣ 执行业务逻辑
                var businessResult = await next();

                // 4️⃣ 保存处理结果
                if (businessResult.IsSuccess)
                {
                    var inboxMessage = new InboxMessage
                    {
                        MessageId = messageId,
                        MessageType = GetMessageType(request),
                        Payload = SerializeRequest(request),
                        ProcessingResult = SerializeResult(businessResult.Value),
                        ProcessedAt = DateTime.UtcNow,
                        Status = InboxStatus.Processed,
                        CorrelationId = GetCorrelationId(request)
                    };

                    await _persistence.MarkAsProcessedAsync(inboxMessage, cancellationToken);

                    _logger.LogInformation("[Inbox] Marked message {MessageId} as processed", messageId);
                }
                else
                {
                    // 处理失败，释放锁定（允许重试）
                    await _persistence.ReleaseLockAsync(messageId, cancellationToken);

                    _logger.LogWarning("[Inbox] Message {MessageId} processing failed: {Error}",
                        messageId, businessResult.Error);
                }

                return businessResult;
            }
            catch (Exception ex)
            {
                // 异常，释放锁定
                await _persistence.ReleaseLockAsync(messageId, cancellationToken);

                _logger.LogError(ex, "[Inbox] Error processing message {MessageId}", messageId);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Inbox] Error in inbox behavior for {RequestType}", typeof(TRequest).Name);
            throw;
        }
    }

    private string GenerateMessageId(TRequest request)
    {
        if (request is IMessage message && !string.IsNullOrEmpty(message.MessageId))
            return message.MessageId;

        return Guid.NewGuid().ToString("N");
    }

    private string GetMessageType(TRequest request)
    {
        return typeof(TRequest).AssemblyQualifiedName
            ?? typeof(TRequest).FullName
            ?? typeof(TRequest).Name;
    }

    private string? GetCorrelationId(TRequest request)
    {
        return request is IMessage message ? message.CorrelationId : null;
    }

    private string SerializeRequest(TRequest request)
    {
        if (_serializer != null)
        {
            var bytes = _serializer.Serialize(request);
            return Convert.ToBase64String(bytes);
        }

        return request.ToString() ?? string.Empty;
    }

    private string? SerializeResult(TResponse? result)
    {
        if (result == null)
            return null;

        if (_serializer != null)
        {
            var bytes = _serializer.Serialize(result);
            return Convert.ToBase64String(bytes);
        }

        return result.ToString();
    }

    private TResponse? DeserializeResult(string data)
    {
        if (_serializer != null)
        {
            var bytes = Convert.FromBase64String(data);
            return _serializer.Deserialize<TResponse>(bytes);
        }

        // 回退到默认值
        return default;
    }
}

