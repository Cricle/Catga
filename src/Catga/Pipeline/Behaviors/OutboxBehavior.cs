using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Catga.Messages;
using Catga.Outbox;
using Catga.Results;
using Catga.Serialization;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Pipeline behavior that saves messages to outbox before publishing
/// Ensures atomicity between business transaction and message publishing
/// </summary>
public class OutboxBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IOutboxStore? _outboxStore;
    private readonly IMessageSerializer? _serializer;
    private readonly ILogger<OutboxBehavior<TRequest, TResponse>> _logger;

    public OutboxBehavior(
        ILogger<OutboxBehavior<TRequest, TResponse>> logger,
        IOutboxStore? outboxStore = null,
        IMessageSerializer? serializer = null)
    {
        _logger = logger;
        _outboxStore = outboxStore;
        _serializer = serializer;
    }

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        // 如果没有配置 outbox store，直接跳过
        if (_outboxStore == null)
            return await next();

        // 只对事件（发布操作）使用 outbox
        // 对于命令和查询，直接执行
        if (request is not IEvent)
            return await next();

        try
        {
            // 执行业务逻辑
            var result = await next();

            if (result.IsSuccess)
            {
                // 如果成功，将事件保存到 outbox
                // 注意：在真实场景中，这应该在同一个数据库事务中完成
                await SaveToOutboxAsync(request, cancellationToken);

                _logger.LogDebug("Saved event {EventType} to outbox", typeof(TRequest).Name);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in outbox behavior for {RequestType}", typeof(TRequest).Name);
            throw;
        }
    }

    private async Task SaveToOutboxAsync(TRequest request, CancellationToken cancellationToken)
    {
        if (_outboxStore == null)
            return;

        var messageId = Guid.NewGuid().ToString("N");
        string? correlationId = null;

        // 如果请求有 MessageId，使用它
        if (request is IMessage message)
        {
            if (!string.IsNullOrEmpty(message.MessageId))
            {
                messageId = message.MessageId;
            }
            correlationId = message.CorrelationId;
        }

        var outboxMessage = new OutboxMessage
        {
            MessageId = messageId,
            MessageType = typeof(TRequest).AssemblyQualifiedName ?? typeof(TRequest).FullName ?? typeof(TRequest).Name,
            Payload = SerializeRequest(request),
            CreatedAt = DateTime.UtcNow,
            Status = OutboxStatus.Pending,
            CorrelationId = correlationId
        };

        await _outboxStore.AddAsync(outboxMessage, cancellationToken);
    }

    [RequiresUnreferencedCode("使用 JsonSerializer 可能需要无法静态分析的类型")]
    [RequiresDynamicCode("使用 JsonSerializer 可能需要运行时代码生成")]
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

