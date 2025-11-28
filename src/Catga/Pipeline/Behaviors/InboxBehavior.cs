using System.Diagnostics.CodeAnalysis;
using Catga;
using Catga.Abstractions;
using Catga.Core;
using Catga.Inbox;
using Microsoft.Extensions.Logging;
using Catga.Observability;

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
            CatgaLog.InboxNoMessageId(_logger, TypeNameCache<TRequest>.Name);
            System.Diagnostics.Activity.Current?.AddActivityEvent("Inbox.NoMessageId",
                ("request.type", TypeNameCache<TRequest>.Name));
            return await next();
        }

        var id = messageId.Value;

        try
        {
            var hasBeenProcessed = await _persistence.HasBeenProcessedAsync(id, cancellationToken);
            if (hasBeenProcessed)
            {
                CatgaLog.InboxAlreadyProcessed(_logger, id);
                System.Diagnostics.Activity.Current?.AddActivityEvent("Inbox.AlreadyProcessed",
                    ("message.id", id));
                var cachedBytes = await _persistence.GetProcessedResultAsync(id, cancellationToken);
                if (cachedBytes != null && cachedBytes.Length > 0)
                {
                    try
                    {
                        var result = _serializer.Deserialize<CatgaResult<TResponse>>(cachedBytes);
                        if (result != default)
                        {
                            System.Diagnostics.Activity.Current?.AddActivityEvent("Inbox.CachedResultReturned",
                                ("message.id", id));
                            return result;
                        }
                    }
                    catch
                    {
                        // fall through
                    }
                }
                CatgaLog.InboxCachedResultDeserializeFailed(_logger, id);
                return CatgaResult<TResponse>.Success(default!);
            }

            var lockAcquired = await _persistence.TryLockMessageAsync(id, _lockDuration, cancellationToken);
            if (!lockAcquired)
            {
                CatgaLog.InboxLockFailed(_logger, id);
                System.Diagnostics.Activity.Current?.AddActivityEvent("Inbox.LockFailed",
                    ("message.id", id));
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
                    MessageType = TypeNameCache<TRequest>.FullName,
                    Payload = _serializer.Serialize(request),
                    ProcessingResult = _serializer.Serialize(result),
                    CorrelationId = request is IMessage corrMsg ? corrMsg.CorrelationId : null
                };
                await _persistence.MarkAsProcessedAsync(inboxMessage, cancellationToken);
                CatgaLog.InboxProcessed(_logger, id);
                System.Diagnostics.Activity.Current?.AddActivityEvent("Inbox.Processed",
                    ("message.id", id));
                return result;
            }
            catch (Exception ex)
            {
                await _persistence.ReleaseLockAsync(id, cancellationToken);
                CatgaLog.InboxProcessingError(_logger, ex, id);
                System.Diagnostics.Activity.Current?.SetError(ex);
                return CatgaResult<TResponse>.Failure(ErrorInfo.FromException(ex, ErrorCodes.PersistenceFailed, isRetryable: true));
            }
        }
        catch (Exception ex)
        {
            CatgaLog.InboxBehaviorError(_logger, ex, id);
            System.Diagnostics.Activity.Current?.SetError(ex);
            return CatgaResult<TResponse>.Failure(ErrorInfo.FromException(ex, ErrorCodes.PersistenceFailed, isRetryable: true));
        }
    }
}

