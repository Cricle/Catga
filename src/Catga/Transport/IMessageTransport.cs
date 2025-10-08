using System.Diagnostics.CodeAnalysis;

namespace Catga.Transport;

/// <summary>
/// Message transport interface - handles message sending and receiving
/// Separated from Outbox/Inbox storage following Single Responsibility Principle
/// </summary>
public interface IMessageTransport
{
    /// <summary>
    /// Publish message to transport layer
    /// </summary>
    [RequiresUnreferencedCode("Message serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Message serialization may require runtime code generation")]
    public Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        TMessage message,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class;

    /// <summary>
    /// Send message to specific destination
    /// </summary>
    [RequiresUnreferencedCode("Message serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Message serialization may require runtime code generation")]
    public Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        TMessage message,
        string destination,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class;

    /// <summary>
    /// Subscribe to messages
    /// </summary>
    [RequiresUnreferencedCode("Message deserialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Message deserialization may require runtime code generation")]
    public Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicConstructors)] TMessage>(
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken cancellationToken = default)
        where TMessage : class;

    /// <summary>
    /// Transport name (NATS, Redis, RabbitMQ, etc.)
    /// </summary>
    public string Name { get; }
}

/// <summary>
/// Transport context - carries message metadata
/// </summary>
public class TransportContext
{
    public string? MessageId { get; set; }
    public string? CorrelationId { get; set; }
    public string? MessageType { get; set; }
    public DateTime? SentAt { get; set; }
    public int RetryCount { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

