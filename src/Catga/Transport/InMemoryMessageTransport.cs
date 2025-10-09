using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Catga.Transport;

/// <summary>
/// In-memory message transport - for testing and local development
/// Supports batching and compression options (but no-op in memory)
/// </summary>
public class InMemoryMessageTransport : IMessageTransport
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _subscribers = new();

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

        // Avoid LINQ allocations - use direct array
        var tasks = new Task[handlers.Count];
        for (int i = 0; i < handlers.Count; i++)
        {
            var handler = (Func<TMessage, TransportContext, Task>)handlers[i];
            tasks[i] = handler(message, context);
        }

        await Task.WhenAll(tasks);
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

