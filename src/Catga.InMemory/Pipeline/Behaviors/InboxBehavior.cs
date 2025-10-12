using System.Diagnostics.CodeAnalysis;
using Catga.Common;
using Catga.Core;
using Catga.Inbox;
using Catga.Messages;
using Catga.Results;
using Catga.Serialization;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>Inbox behavior for message idempotency (storage-layer deduplication)</summary>
public class InboxBehavior<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : class, IRequest<TResponse>
{
    private readonly IInboxStore? _persistence;
    private readonly IMessageSerializer? _serializer;
    private readonly ILogger<InboxBehavior<TRequest, TResponse>> _logger;
    private readonly TimeSpan _lockDuration;

    public InboxBehavior(ILogger<InboxBehavior<TRequest, TResponse>> logger, IInboxStore? persistence = null, IMessageSerializer? serializer = null, TimeSpan? lockDuration = null)
    {
        _logger = logger;
        _persistence = persistence;
        _serializer = serializer;
        _lockDuration = lockDuration ?? TimeSpan.FromMinutes(5);
    }

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        if (_persistence == null) return await next();

        string? messageId = null;
        if (request is IMessage message && !string.IsNullOrEmpty(message.MessageId))
            messageId = message.MessageId;

        if (string.IsNullOrEmpty(messageId))
        {
            _logger.LogDebug("No MessageId found for {RequestType}, skipping inbox check", TypeNameCache<TRequest>.Name);
            return await next();
        }

        try
        {
            var hasBeenProcessed = await _persistence.HasBeenProcessedAsync(messageId, cancellationToken);
            if (hasBeenProcessed)
            {
                _logger.LogInformation("Message {MessageId} has already been processed, returning cached result", messageId);
                var cachedResult = await _persistence.GetProcessedResultAsync(messageId, cancellationToken);
                if (!string.IsNullOrEmpty(cachedResult) && 
                    SerializationHelper.TryDeserialize<CatgaResult<TResponse>>(cachedResult, out var result, _serializer))
                    return result;
                _logger.LogWarning("Failed to deserialize cached result for message {MessageId}, returning default success", messageId);
                return CatgaResult<TResponse>.Success(default!);
            }

            var lockAcquired = await _persistence.TryLockMessageAsync(messageId, _lockDuration, cancellationToken);
            if (!lockAcquired)
            {
                _logger.LogWarning("Failed to acquire lock for message {MessageId}, another instance may be processing it", messageId);
                return CatgaResult<TResponse>.Failure("Message is being processed by another instance");
            }

            try
            {
                var result = await next();
                var inboxMessage = new InboxMessage
                {
                    MessageId = messageId,
                    MessageType = MessageHelper.GetMessageType<TRequest>(),
                    Payload = SerializationHelper.Serialize(request, _serializer),
                    ProcessingResult = SerializationHelper.Serialize(result, _serializer),
                    CorrelationId = MessageHelper.GetCorrelationId(request)
                };
                await _persistence.MarkAsProcessedAsync(inboxMessage, cancellationToken);
                _logger.LogDebug("Marked message {MessageId} as processed in inbox", messageId);
                return result;
            }
            catch (Exception)
            {
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
}

