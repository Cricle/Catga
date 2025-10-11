using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Catga.Idempotency;
using Catga.Messages;

namespace Catga.Transport;

/// <summary>
/// In-memory message transport - for testing and local development
/// Supports QoS (Quality of Service) levels
/// </summary>
public class InMemoryMessageTransport : IMessageTransport
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _subscribers = new();
    private readonly InMemoryIdempotencyStore _idempotencyStore = new();

    public string Name => "InMemory";

    public BatchTransportOptions? BatchOptions => null; // Not applicable for in-memory

    public CompressionTransportOptions? CompressionOptions => null; // Not applicable for in-memory
    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        TMessage message,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var messageType = typeof(TMessage);
        if (!_subscribers.TryGetValue(messageType, out var handlers))
            return;

        context ??= new TransportContext
        {
            MessageId = Guid.NewGuid().ToString(),
            MessageType = messageType.FullName,
            SentAt = DateTime.UtcNow
        };

        var qos = (message as IMessage)?.QoS ?? QualityOfService.AtLeastOnce;
        var deliveryMode = (message as IMessage)?.DeliveryMode ?? DeliveryMode.WaitForResult;

        switch (qos)
        {
            case QualityOfService.AtMostOnce:
                // QoS 0: Fire-and-forget, no wait
                _ = FireAndForgetAsync(handlers, message, context, cancellationToken);
                break;

            case QualityOfService.AtLeastOnce:
                // QoS 1: At-least-once delivery
                if (deliveryMode == DeliveryMode.WaitForResult)
                {
                    // Wait for result (synchronous)
                    var tasks = new Task[handlers.Count];
                    for (int i = 0; i < handlers.Count; i++)
                    {
                        var handler = (Func<TMessage, TransportContext, Task>)handlers[i];
                        tasks[i] = handler(message, context);
                    }
                    await Task.WhenAll(tasks);
                }
                else
                {
                    // Async retry (background delivery)
                    _ = DeliverWithRetryAsync(handlers, message, context, cancellationToken);
                }
                break;

            case QualityOfService.ExactlyOnce:
                // QoS 2: Exactly-once with idempotency check
                if (context.MessageId != null && _idempotencyStore.IsProcessed(context.MessageId))
                {
                    return;
                }

                var tasks2 = new Task[handlers.Count];
                for (int i = 0; i < handlers.Count; i++)
                {
                    var handler = (Func<TMessage, TransportContext, Task>)handlers[i];
                    tasks2[i] = handler(message, context);
                }
                await Task.WhenAll(tasks2);

                if (context.MessageId != null)
                {
                    _idempotencyStore.MarkAsProcessed(context.MessageId);
                }
                break;
        }
    }

    /// <summary>
    /// Fire-and-forget async execution (QoS 0)
    /// </summary>
    private static async ValueTask FireAndForgetAsync<TMessage>(
        List<Delegate> handlers,
        TMessage message,
        TransportContext context,
        CancellationToken cancellationToken) where TMessage : class
    {
        try
        {
            var tasks = new Task[handlers.Count];
            for (int i = 0; i < handlers.Count; i++)
            {
                var handler = (Func<TMessage, TransportContext, Task>)handlers[i];
                tasks[i] = handler(message, context);
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            // Ignore exceptions for fire-and-forget
        }
    }

    /// <summary>
    /// Async delivery with retry (QoS 1 with exponential backoff)
    /// </summary>
    private static async ValueTask DeliverWithRetryAsync<TMessage>(
        List<Delegate> handlers,
        TMessage message,
        TransportContext context,
        CancellationToken cancellationToken) where TMessage : class
    {
        const int maxRetries = 3;
        var baseDelay = TimeSpan.FromMilliseconds(100);

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var tasks = new Task[handlers.Count];
                for (int i = 0; i < handlers.Count; i++)
                {
                    var handler = (Func<TMessage, TransportContext, Task>)handlers[i];
                    tasks[i] = handler(message, context);
                }
                await Task.WhenAll(tasks).ConfigureAwait(false);
                return;
            }
            catch when (attempt < maxRetries)
            {
                // Retry with exponential backoff
                var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Final retry failed, should log to dead-letter queue in production
            }
        }
    }
    public Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        TMessage message,
        string destination,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        // For in-memory transport, Send and Publish behave the same
        return PublishAsync(message, context, cancellationToken);
    }
    public Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicConstructors)] TMessage>(
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var messageType = typeof(TMessage);
        _subscribers.AddOrUpdate(
            messageType,
            _ => new List<Delegate> { handler },
            (_, list) =>
            {
                list.Add(handler);
                return list;
            });

        return Task.CompletedTask;
    }
    public async Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        IEnumerable<TMessage> messages,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        // For in-memory, just publish each message
        foreach (var message in messages)
        {
            await PublishAsync(message, context, cancellationToken);
        }
    }
    public async Task SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        IEnumerable<TMessage> messages,
        string destination,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        // For in-memory, Send and Publish behave the same
        await PublishBatchAsync(messages, context, cancellationToken);
    }
}

