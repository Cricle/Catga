using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Catga.Inbox;
using Catga.Messages;
using Catga.Results;
using Catga.Serialization;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Pipeline behavior that ensures idempotent message processing using inbox pattern
/// Prevents duplicate processing of the same message
/// </summary>
/// <remarks>
/// 注意：此 Behavior 使用序列化功能，在 NativeAOT 环境下需要 AOT 友好的序列化器（如 MemoryPack）。
/// 或者在不需要 Inbox 模式时，不要注册此 Behavior。
/// </remarks>
[RequiresUnreferencedCode("Inbox Behavior 需要序列化消息。在 AOT 环境下请使用 MemoryPack 等 AOT 友好的序列化器，或不使用此 Behavior。")]
[RequiresDynamicCode("Inbox Behavior 需要序列化消息。在 AOT 环境下请使用 MemoryPack 等 AOT 友好的序列化器，或不使用此 Behavior。")]
public class InboxBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IInboxStore? _inboxStore;
    private readonly IMessageSerializer? _serializer;
    private readonly ILogger<InboxBehavior<TRequest, TResponse>> _logger;
    private readonly TimeSpan _lockDuration;

    public InboxBehavior(
        ILogger<InboxBehavior<TRequest, TResponse>> logger,
        IInboxStore? inboxStore = null,
        IMessageSerializer? serializer = null,
        TimeSpan? lockDuration = null)
    {
        _logger = logger;
        _inboxStore = inboxStore;
        _serializer = serializer;
        _lockDuration = lockDuration ?? TimeSpan.FromMinutes(5);
    }

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        // 如果没有配置 inbox store，直接执行
        if (_inboxStore == null)
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
            var hasBeenProcessed = await _inboxStore.HasBeenProcessedAsync(messageId, cancellationToken);
            if (hasBeenProcessed)
            {
                _logger.LogInformation("Message {MessageId} has already been processed, returning cached result", messageId);

                // 获取缓存的结果
                var cachedResult = await _inboxStore.GetProcessedResultAsync(messageId, cancellationToken);
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
            var lockAcquired = await _inboxStore.TryLockMessageAsync(messageId, _lockDuration, cancellationToken);
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

                await _inboxStore.MarkAsProcessedAsync(inboxMessage, cancellationToken);

                _logger.LogDebug("Marked message {MessageId} as processed in inbox", messageId);

                return result;
            }
            catch (Exception)
            {
                // 处理失败，释放锁
                await _inboxStore.ReleaseLockAsync(messageId, cancellationToken);
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

