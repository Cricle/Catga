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

    [RequiresUnreferencedCode("Message serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Message serialization may require runtime code generation")]
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

        // Get QoS level from message
        var qos = (message as IMessage)?.QoS ?? QualityOfService.AtLeastOnce;

        switch (qos)
        {
            case QualityOfService.AtMostOnce:
                // QoS 0: Fire-and-forget (不等待完成)
                _ = Task.Run(async () =>
                {
                    var tasks = new Task[handlers.Count];
                    for (int i = 0; i < handlers.Count; i++)
                    {
                        var handler = (Func<TMessage, TransportContext, Task>)handlers[i];
                        tasks[i] = handler(message, context);
                    }
                    await Task.WhenAll(tasks);
                }, cancellationToken);
                break;

            case QualityOfService.AtLeastOnce:
                // QoS 1: Wait for completion (等待完成)
                var tasks = new Task[handlers.Count];
                for (int i = 0; i < handlers.Count; i++)
                {
                    var handler = (Func<TMessage, TransportContext, Task>)handlers[i];
                    tasks[i] = handler(message, context);
                }
                await Task.WhenAll(tasks);
                break;

            case QualityOfService.ExactlyOnce:
                // QoS 2: Idempotency check + wait for completion (幂等性检查 + 等待完成)
                if (_idempotencyStore.IsProcessed(context.MessageId))
                {
                    // 已处理过，跳过
                    return;
                }

                var tasks2 = new Task[handlers.Count];
                for (int i = 0; i < handlers.Count; i++)
                {
                    var handler = (Func<TMessage, TransportContext, Task>)handlers[i];
                    tasks2[i] = handler(message, context);
                }
                await Task.WhenAll(tasks2);

                // 标记为已处理
                _idempotencyStore.MarkAsProcessed(context.MessageId);
                break;
        }
    }

    [RequiresUnreferencedCode("Message serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Message serialization may require runtime code generation")]
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

    [RequiresUnreferencedCode("Message deserialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Message deserialization may require runtime code generation")]
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

    [RequiresUnreferencedCode("Message serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Message serialization may require runtime code generation")]
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

    [RequiresUnreferencedCode("Message serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Message serialization may require runtime code generation")]
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

