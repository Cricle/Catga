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
/// Outbox Behavior - Separates storage and transport concerns
///
/// Architecture:
/// - IOutboxStore: Responsible for persistent storage (Redis, SQL, MongoDB, etc.)
/// - IMessageTransport: Responsible for message transport (NATS, Redis Pub/Sub, RabbitMQ, etc.)
/// - Both are independently configured, adhering to Single Responsibility Principle
///
/// Flow:
/// 1. Save to Outbox store (synchronized with business transaction)
/// 2. Execute business logic
/// 3. Publish message via transport layer
/// 4. Mark as published (or failed for retry)
/// </summary>
/// <remarks>
/// Inspired by MassTransit design: transport and persistence separation
/// - Transport layer is swappable (NATS/Redis/RabbitMQ)
/// - Storage layer is swappable (SQL/Redis/MongoDB)
/// - Independent evolution
/// </remarks>
[RequiresUnreferencedCode("Outbox behavior requires serialization and transport support. Use AOT-friendly serializer (e.g., MemoryPack) in production")]
[RequiresDynamicCode("Outbox behavior requires serialization and transport support. Use AOT-friendly serializer (e.g., MemoryPack) in production")]
public class OutboxBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : class, IRequest<TResponse>
{
    private readonly IOutboxStore? _persistence;           // Storage layer
    private readonly IMessageTransport? _transport;       // Transport layer
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
        // If no storage or transport configured, skip
        if (_persistence == null || _transport == null)
            return await next();

        // Only use outbox for events (publish operations)
        if (request is not IEvent)
            return await next();

        var messageId = GenerateMessageId(request);

        try
        {
            // 1️⃣ Save to persistent storage (in the same transaction as business logic)
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

            // 2️⃣ Execute business logic
            var result = await next();

            // 3️⃣ If business logic succeeds, publish message via transport layer
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

                    // 4️⃣ Mark as published
                    await _persistence.MarkAsPublishedAsync(messageId, cancellationToken);

                    _logger.LogInformation("[Outbox] Published message {MessageId} via {Transport}",
                        messageId, _transport.Name);
                }
                catch (Exception ex)
                {
                    // Transport failed, mark for retry (background service will retry)
                    await _persistence.MarkAsFailedAsync(messageId, ex.Message, cancellationToken);

                    _logger.LogError(ex, "[Outbox] Failed to publish message {MessageId}, marked for retry", messageId);

                    // Note: Can choose whether to throw exception here
                    // If thrown, business transaction will roll back
                    // If not thrown, message will enter retry queue
                    // throw; // Optional
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

        // Fallback to JsonSerializer (with warning)
        return JsonSerializer.Serialize(request);
    }
}

