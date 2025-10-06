using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Catga.Messages;
using Catga.Outbox;
using Catga.Results;
using Catga.Serialization;
using Catga.Transport;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Outbox 行为 - 分离存储和传输关注点
///
/// 架构说明：
/// - IOutboxStore: 负责持久化存储（可用 Redis, SQL, MongoDB 等）
/// - IMessageTransport: 负责消息传输（NATS, Redis Pub/Sub, RabbitMQ 等）
/// - 两者独立配置，遵循单一职责原则
///
/// 流程：
/// 1. 保存到 Outbox 存储（与业务事务同步）
/// 2. 执行业务逻辑
/// 3. 通过传输层发布消息
/// 4. 标记为已发布（或失败重试）
/// </summary>
/// <remarks>
/// 参考 MassTransit 的设计：传输和持久化分离
/// - 传输层可切换（NATS/Redis/RabbitMQ）
/// - 存储层可切换（SQL/Redis/MongoDB）
/// - 互不影响，独立演进
/// </remarks>
[RequiresUnreferencedCode("Outbox 行为需要序列化和传输支持。生产环境请使用 AOT 友好的序列化器（如 MemoryPack）")]
[RequiresDynamicCode("Outbox 行为需要序列化和传输支持。生产环境请使用 AOT 友好的序列化器（如 MemoryPack）")]
public class OutboxBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : class, IRequest<TResponse>
{
    private readonly IOutboxStore? _persistence;           // 存储层
    private readonly IMessageTransport? _transport;       // 传输层
    private readonly IMessageSerializer? _serializer;
    private readonly ILogger<OutboxBehavior<TRequest, TResponse>> _logger;

    public OutboxBehavior(
        ILogger<OutboxBehavior<TRequest, TResponse>> logger,
        IOutboxStore? persistence = null,
        IMessageTransport? transport = null,
        IMessageSerializer? serializer = null)
    {
        _logger = logger;
        _persistence = persistence;
        _transport = transport;
        _serializer = serializer;
    }

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        // 如果没有配置存储或传输，直接跳过
        if (_persistence == null || _transport == null)
            return await next();

        // 只对事件（发布操作）使用 outbox
        if (request is not IEvent)
            return await next();

        var messageId = GenerateMessageId(request);

        try
        {
            // 1️⃣ 保存到持久化存储（与业务事务在同一个事务中）
            var outboxMessage = new OutboxMessage
            {
                MessageId = messageId,
                MessageType = GetMessageType(request),
                Payload = SerializeRequest(request),
                CreatedAt = DateTime.UtcNow,
                Status = OutboxStatus.Pending,
                CorrelationId = GetCorrelationId(request)
            };

            await _persistence.AddAsync(outboxMessage, cancellationToken);

            _logger.LogDebug("[Outbox] Saved message {MessageId} to persistence store ({Store})",
                messageId, _persistence.GetType().Name);

            // 2️⃣ 执行业务逻辑
            var result = await next();

            // 3️⃣ 如果业务成功，通过传输层发布消息
            if (result.IsSuccess)
            {
                try
                {
                    var context = new TransportContext
                    {
                        MessageId = messageId,
                        MessageType = outboxMessage.MessageType,
                        CorrelationId = outboxMessage.CorrelationId,
                        SentAt = DateTime.UtcNow
                    };

                    await _transport.PublishAsync<TRequest>(request, context, cancellationToken);

                    // 4️⃣ 标记为已发布
                    await _persistence.MarkAsPublishedAsync(messageId, cancellationToken);

                    _logger.LogInformation("[Outbox] Published message {MessageId} via {Transport}",
                        messageId, _transport.Name);
                }
                catch (Exception ex)
                {
                    // 传输失败，标记为待重试（后台服务会重试）
                    await _persistence.MarkAsFailedAsync(messageId, ex.Message, cancellationToken);

                    _logger.LogError(ex, "[Outbox] Failed to publish message {MessageId}, marked for retry", messageId);

                    // 注意：这里可以选择是否抛出异常
                    // 如果抛出，业务事务会回滚
                    // 如果不抛出，消息会进入重试队列
                    // throw; // 可选
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Outbox] Error in outbox behavior for {RequestType}", typeof(TRequest).Name);
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

        // 回退到 JsonSerializer（带警告）
        return JsonSerializer.Serialize(request);
    }
}

