using Catga;
using Catga.Abstractions;
using Catga.Core;
using Catga.DistributedId;
using Catga.Outbox;
using Catga.Transport;
using Microsoft.Extensions.Logging;
using Catga.Observability;

namespace Catga.Pipeline.Behaviors;

/// <summary>Outbox behavior - storage/transport separation (inspired by MassTransit)</summary>
/// <remarks>
/// Requires IOutboxStore, IMessageTransport, and IMessageSerializer. If any is not registered, behavior is skipped.
/// For production AOT, use MemoryPack serializer.
/// </remarks>
public class OutboxBehavior<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TRequest, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : class, IRequest<TResponse>
{
    private readonly IOutboxStore _persistence;
    private readonly IMessageTransport _transport;
    private readonly IMessageSerializer _serializer;
    private readonly IDistributedIdGenerator _idGenerator;
    private readonly ILogger<OutboxBehavior<TRequest, TResponse>> _logger;

    public OutboxBehavior(
        ILogger<OutboxBehavior<TRequest, TResponse>> logger,
        IDistributedIdGenerator idGenerator,
        IOutboxStore persistence,
        IMessageTransport transport,
        IMessageSerializer serializer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        if (request is not IEvent) return await next();

        var messageId = MessageHelper.GetOrGenerateMessageId(request, _idGenerator);

        try
        {
            var outboxMessage = new OutboxMessage
            {
                MessageId = messageId,
                MessageType = MessageHelper.GetMessageType<TRequest>(),
                Payload = _serializer.Serialize(request),
                CreatedAt = DateTime.UtcNow,
                Status = OutboxStatus.Pending,
                CorrelationId = MessageHelper.GetCorrelationId(request)
            };

            await _persistence.AddAsync(outboxMessage, cancellationToken);
            CatgaLog.OutboxSaved(_logger, messageId, _persistence.GetType().Name);

            var result = await next();

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
                    await _persistence.MarkAsPublishedAsync(messageId, cancellationToken);
                    CatgaLog.OutboxPublished(_logger, messageId, _transport.Name);
                }
                catch (Exception ex)
                {
                    await _persistence.MarkAsFailedAsync(messageId, ex.Message, cancellationToken);
                    CatgaLog.OutboxPublishFailed(_logger, ex, messageId);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            CatgaLog.OutboxBehaviorError(_logger, ex, TypeNameCache<TRequest>.Name);
            return CatgaResult<TResponse>.Failure(ErrorInfo.FromException(ex, ErrorCodes.PersistenceFailed, isRetryable: true));
        }
    }
}

