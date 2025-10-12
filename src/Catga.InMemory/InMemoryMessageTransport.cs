using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Catga.Idempotency;
using Catga.Messages;

namespace Catga.Transport;

/// <summary>In-memory message transport (for testing, supports QoS)</summary>
public class InMemoryMessageTransport : IMessageTransport
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _subscribers = new();
    private readonly InMemoryIdempotencyStore _idempotencyStore = new();

    public string Name => "InMemory";
    public BatchTransportOptions? BatchOptions => null;
    public CompressionTransportOptions? CompressionOptions => null;
    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
    {
        var messageType = typeof(TMessage);
        if (!_subscribers.TryGetValue(messageType, out var handlers)) return;

        context ??= new TransportContext { MessageId = Guid.NewGuid().ToString(), MessageType = messageType.FullName, SentAt = DateTime.UtcNow };

        var qos = (message as IMessage)?.QoS ?? QualityOfService.AtLeastOnce;
        var deliveryMode = (message as IMessage)?.DeliveryMode ?? DeliveryMode.WaitForResult;

        switch (qos)
        {
            case QualityOfService.AtMostOnce:
                _ = FireAndForgetAsync(handlers, message, context, cancellationToken);
                break;

            case QualityOfService.AtLeastOnce:
                if (deliveryMode == DeliveryMode.WaitForResult)
                {
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
                    _ = DeliverWithRetryAsync(handlers, message, context, cancellationToken);
                }
                break;

            case QualityOfService.ExactlyOnce:
                if (context.MessageId != null && _idempotencyStore.IsProcessed(context.MessageId)) return;

                var tasks2 = new Task[handlers.Count];
                for (int i = 0; i < handlers.Count; i++)
                {
                    var handler = (Func<TMessage, TransportContext, Task>)handlers[i];
                    tasks2[i] = handler(message, context);
                }
                await Task.WhenAll(tasks2);

                if (context.MessageId != null)
                    _idempotencyStore.MarkAsProcessed(context.MessageId);
                break;
        }
    }

    private static async ValueTask FireAndForgetAsync<TMessage>(List<Delegate> handlers, TMessage message, TransportContext context, CancellationToken cancellationToken) where TMessage : class
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
        }
    }

    private static async ValueTask DeliverWithRetryAsync<TMessage>(List<Delegate> handlers, TMessage message, TransportContext context, CancellationToken cancellationToken) where TMessage : class
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
                var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }
    public Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message, string destination, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
        => PublishAsync(message, context, cancellationToken);

    public Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(Func<TMessage, TransportContext, Task> handler, CancellationToken cancellationToken = default) where TMessage : class
    {
        var messageType = typeof(TMessage);
        _subscribers.AddOrUpdate(messageType, _ => new List<Delegate> { handler }, (_, list) =>
        {
            list.Add(handler);
            return list;
        });
        return Task.CompletedTask;
    }

    public async Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(IEnumerable<TMessage> messages, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
    {
        foreach (var message in messages)
            await PublishAsync(message, context, cancellationToken);
    }

    public async Task SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(IEnumerable<TMessage> messages, string destination, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
        => await PublishBatchAsync(messages, context, cancellationToken);
}

