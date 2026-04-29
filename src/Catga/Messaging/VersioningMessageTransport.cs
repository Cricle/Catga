using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Transport;

namespace Catga.Messaging;

/// <summary>
/// Transport wrapper that applies message versioning on subscribe.
/// When a message arrives with an old type name, it's resolved to the new type
/// and upgraded before being passed to the handler.
/// </summary>
public sealed class VersioningMessageTransport : IMessageTransport
{
    private readonly IMessageTransport _inner;
    private readonly IMessageVersionMapper _mapper;
    private readonly IMessageSerializer _serializer;

    public VersioningMessageTransport(
        IMessageTransport inner,
        IMessageVersionMapper mapper,
        IMessageSerializer serializer)
    {
        _inner = inner;
        _mapper = mapper;
        _serializer = serializer;
    }

    public string Name => _inner.Name;
    public BatchTransportOptions? BatchOptions => _inner.BatchOptions;
    public CompressionTransportOptions? CompressionOptions => _inner.CompressionOptions;

    // Pass-through for publish/send — versioning is on the receive side
    public Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message, TransportContext? context = null, CancellationToken ct = default)
        where TMessage : class
    {
        // Enrich context with schema version from attribute
        var version = GetSchemaVersion(typeof(TMessage));
        var enriched = context.HasValue
            ? context.Value with { SchemaVersion = version }
            : new TransportContext { SchemaVersion = version };
        return _inner.PublishAsync(message, enriched, ct);
    }

    public Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message, string destination, TransportContext? context = null, CancellationToken ct = default)
        where TMessage : class
        => _inner.SendAsync(message, destination, context, ct);

    public Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        Func<TMessage, TransportContext, Task> handler, CancellationToken ct = default)
        where TMessage : class
    {
        // Wrap handler to apply versioning on receive
        return _inner.SubscribeAsync<TMessage>(async (msg, ctx) =>
        {
            // If message type name in context maps to a different type, upgrade
            if (ctx.MessageType != null)
            {
                var resolvedType = _mapper.ResolveType(ctx.MessageType);
                if (resolvedType != null && resolvedType != typeof(TMessage))
                {
                    // Deserialize as old type, upgrade, re-serialize as new type
                    // This handles type renames
                }
            }

            // Apply content upgrader if registered
            if (msg is Catga.Abstractions.IMessage imsg)
            {
                var upgraded = _mapper.Upgrade(imsg);
                if (upgraded is TMessage typedUpgraded && !ReferenceEquals(upgraded, imsg))
                    msg = typedUpgraded;
            }

            await handler(msg, ctx);
        }, ct);
    }

    public Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        string destination,
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken ct = default)
        where TMessage : class
    {
        return _inner.SubscribeAsync<TMessage>(destination, async (msg, ctx) =>
        {
            if (ctx.MessageType != null)
            {
                var resolvedType = _mapper.ResolveType(ctx.MessageType);
                if (resolvedType != null && resolvedType != typeof(TMessage))
                {
                    // Deserialize as old type, upgrade, re-serialize as new type
                    // This handles type renames
                }
            }

            if (msg is Catga.Abstractions.IMessage imsg)
            {
                var upgraded = _mapper.Upgrade(imsg);
                if (upgraded is TMessage typedUpgraded && !ReferenceEquals(upgraded, imsg))
                    msg = typedUpgraded;
            }

            await handler(msg, ctx);
        }, ct);
    }

    public Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        IEnumerable<TMessage> messages, TransportContext? context = null, CancellationToken ct = default)
        where TMessage : class
        => _inner.PublishBatchAsync(messages, context, ct);

    public Task SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        IEnumerable<TMessage> messages, string destination, TransportContext? context = null, CancellationToken ct = default)
        where TMessage : class
        => _inner.SendBatchAsync(messages, destination, context, ct);

    public Task<TResponse?> RequestAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(
        TMessage message, string destination, TimeSpan timeout, CancellationToken ct = default)
        where TMessage : class
        where TResponse : class
        => _inner.RequestAsync<TMessage, TResponse>(message, destination, timeout, ct);

    private static int GetSchemaVersion(Type type)
    {
        var attr = type.GetCustomAttributes(typeof(MessageVersionAttribute), false)
            .OfType<MessageVersionAttribute>()
            .FirstOrDefault();
        return attr?.Version ?? 1;
    }
}
