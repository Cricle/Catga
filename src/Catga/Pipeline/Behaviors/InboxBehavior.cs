using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Catga.Inbox;
using Catga.Messages;
using Catga.Results;
using Catga.Serialization;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Inbox Behavior - Focuses on the storage layer to ensure idempotency
///
/// Architecture:
/// - IInboxStore: Responsible for persistent storage (Redis, SQL, MongoDB, etc.)
/// - Does not involve the transport layer (transport handled by IMessageTransport)
/// - Focuses on message deduplication and idempotency guarantees
///
/// Flow:
/// 1. Attempt to lock message (if already processed, return cached result)
/// 2. Execute business logic
/// 3. Save processing result to Inbox
/// 4. Release lock
/// </summary>
/// <remarks>
/// Inspired by MassTransit design: Inbox focuses on idempotency, not transport
/// </remarks>
[RequiresUnreferencedCode("Inbox behavior requires serialization support. Use AOT-friendly serializer (e.g., MemoryPack) in production")]
[RequiresDynamicCode("Inbox behavior requires serialization support. Use AOT-friendly serializer (e.g., MemoryPack) in production")]
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
        // If inbox is not configured, execute directly
        if (_persistence == null)
            return await next();

        // Get MessageId
        string? messageId = null;
        if (request is IMessage message && !string.IsNullOrEmpty(message.MessageId))
        {
            messageId = message.MessageId;
        }

        // If no MessageId, cannot use inbox pattern, execute directly
        if (string.IsNullOrEmpty(messageId))
        {
            _logger.LogDebug("No MessageId found for {RequestType}, skipping inbox check", typeof(TRequest).Name);
            return await next();
        }

        try
        {
            // Check if message has already been processed
            var hasBeenProcessed = await _persistence.HasBeenProcessedAsync(messageId, cancellationToken);
            if (hasBeenProcessed)
            {
                _logger.LogInformation("Message {MessageId} has already been processed, returning cached result", messageId);

                // Get cached result
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

                // If cached result cannot be deserialized, return success with no value
                return CatgaResult<TResponse>.Success(default!);
            }

            // Attempt to lock message
            var lockAcquired = await _persistence.TryLockMessageAsync(messageId, _lockDuration, cancellationToken);
            if (!lockAcquired)
            {
                _logger.LogWarning("Failed to acquire lock for message {MessageId}, another instance may be processing it", messageId);
                return CatgaResult<TResponse>.Failure("Message is being processed by another instance");
            }

            try
            {
                // Execute business logic
                var result = await next();

                // Save processing result to inbox
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
                // Processing failed, release lock
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

