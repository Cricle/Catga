using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Catga.Inbox;
using Catga.Messages;
using Catga.Serialization;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>Inbox behavior for message idempotency (storage-layer deduplication)</summary>
/// <remarks>
/// Requires both IInboxStore and IMessageSerializer. If either is not registered, behavior is skipped.
/// For production AOT, use MemoryPack serializer.
/// </remarks>
public class InboxBehavior<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : class, IRequest<TResponse>
{
    private readonly IInboxStore _persistence;
    private readonly IMessageSerializer _serializer;
    private readonly ILogger<InboxBehavior<TRequest, TResponse>> _logger;
    private readonly TimeSpan _lockDuration;

    public InboxBehavior(
        ILogger<InboxBehavior<TRequest, TResponse>> logger,
        IInboxStore persistence,
        IMessageSerializer serializer,
        TimeSpan? lockDuration = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _lockDuration = lockDuration ?? TimeSpan.FromMinutes(5);
    }

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        long? messageId = null;
        if (request is IMessage message && message.MessageId != 0)
            messageId = message.MessageId;

        if (!messageId.HasValue)
        {
            _logger.LogDebug("No MessageId found for {RequestType}, skipping inbox check", TypeNameCache<TRequest>.Name);
            return await next();
        }

        var id = messageId.Value;

        try
        {
            var hasBeenProcessed = await _persistence.HasBeenProcessedAsync(id, cancellationToken);
            if (hasBeenProcessed)
            {
                _logger.LogInformation("Message {MessageId} has already been processed, returning cached result", id);
                var cachedResult = await _persistence.GetProcessedResultAsync(id, cancellationToken);
                if (!string.IsNullOrEmpty(cachedResult) &&
                    SerializationHelper.TryDeserialize<CatgaResult<TResponse>>(cachedResult, out var result, _serializer))
                    return result;
                _logger.LogWarning("Failed to deserialize cached result for message {MessageId}, returning default success", id);
                return CatgaResult<TResponse>.Success(default!);
            }

            var lockAcquired = await _persistence.TryLockMessageAsync(id, _lockDuration, cancellationToken);
            if (!lockAcquired)
            {
                _logger.LogWarning("Failed to acquire lock for message {MessageId}, another instance may be processing it", id);
                return CatgaResult<TResponse>.Failure(new ErrorInfo
                {
                    Code = ErrorCodes.LockFailed,
                    Message = "Message is being processed by another instance",
                    IsRetryable = true
                });
            }

            try
            {
                var result = await next();
                var inboxMessage = new InboxMessage
                {
                    MessageId = id,
                    MessageType = MessageHelper.GetMessageType<TRequest>(),
                    Payload = SerializationHelper.Serialize(request, _serializer),
                    ProcessingResult = SerializationHelper.Serialize(result, _serializer),
                    CorrelationId = MessageHelper.GetCorrelationId(request)
                };
                await _persistence.MarkAsProcessedAsync(inboxMessage, cancellationToken);
                _logger.LogDebug("Marked message {MessageId} as processed in inbox", id);
                return result;
            }
            catch (Exception ex)
            {
                await _persistence.ReleaseLockAsync(id, cancellationToken);
                _logger.LogError(ex, "Error processing message {MessageId} in inbox", id);
                return CatgaResult<TResponse>.Failure(ErrorInfo.FromException(ex, ErrorCodes.PersistenceFailed, isRetryable: true));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in inbox behavior for message {MessageId}", id);
            return CatgaResult<TResponse>.Failure(ErrorInfo.FromException(ex, ErrorCodes.PersistenceFailed, isRetryable: true));
        }
    }
}

