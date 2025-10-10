using Catga.Messages;
using Catga.Serialization;
using Catga.Transport;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace Catga.Cluster.DotNext;

/// <summary>
/// Raft-based message transport with automatic leader routing
/// </summary>
public class RaftMessageTransport : IMessageTransport
{
    private readonly ICatgaRaftCluster _cluster;
    private readonly IMessageSerializer _serializer;
    private readonly ILogger<RaftMessageTransport> _logger;

    public string Name => "Raft";
    public BatchTransportOptions? BatchOptions => new();
    public CompressionTransportOptions? CompressionOptions => new();

    public RaftMessageTransport(
        ICatgaRaftCluster cluster,
        IMessageSerializer serializer,
        ILogger<RaftMessageTransport> logger)
    {
        _cluster = cluster;
        _serializer = serializer;
        _logger = logger;
    }

    /// <summary>
    /// Send request (automatically routes Command to Leader, Query locally)
    /// </summary>
    [RequiresUnreferencedCode("Message serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Message serialization may require runtime code generation")]
    public async Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        TMessage message,
        string destination,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(destination);

        // Check if this is a Command (write operation)
        var isCommand = message is IRequest<object>;
        
        if (isCommand)
        {
            // Command: Forward to Leader
            await SendToLeaderAsync(message, destination, context, cancellationToken);
        }
        else
        {
            // Query/Event: Handle locally or broadcast
            await HandleLocallyAsync(message, destination, context, cancellationToken);
        }
    }

    /// <summary>
    /// Publish event (broadcasts to all nodes)
    /// </summary>
    [RequiresUnreferencedCode("Message serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Message serialization may require runtime code generation")]
    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        TMessage message,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(message);

        _logger.LogInformation("Broadcasting event to all nodes: {MessageType}", typeof(TMessage).Name);

        // Serialize message once
        var data = _serializer.Serialize(message);

        // Broadcast to all cluster members
        var tasks = _cluster.Members
            .Where(m => m.Status == ClusterMemberStatus.Available)
            .Select(async member =>
            {
                try
                {
                    // TODO: Implement actual HTTP/gRPC call to member
                    _logger.LogDebug("Sent event to {MemberId}", member.Id);
                    await Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send event to {MemberId}", member.Id);
                }
            });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Subscribe to messages
    /// </summary>
    [RequiresUnreferencedCode("Message deserialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Message deserialization may require runtime code generation")]
    public Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicConstructors)] TMessage>(
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(handler);

        _logger.LogInformation("Subscribing to messages of type {MessageType}", typeof(TMessage).Name);
        
        // TODO: Implement subscription logic
        return Task.CompletedTask;
    }

    /// <summary>
    /// Send batch messages (optimized for throughput)
    /// </summary>
    [RequiresUnreferencedCode("Message serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Message serialization may require runtime code generation")]
    public async Task SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        IEnumerable<TMessage> messages,
        string destination,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(destination);

        _logger.LogInformation("Sending batch of messages to {Destination}", destination);

        // Send all messages (parallel for better throughput)
        await Task.WhenAll(messages.Select(msg => 
            SendAsync(msg, destination, context, cancellationToken)));
    }

    /// <summary>
    /// Publish batch events (broadcasts all events)
    /// </summary>
    [RequiresUnreferencedCode("Message serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Message serialization may require runtime code generation")]
    public async Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        IEnumerable<TMessage> messages,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(messages);

        _logger.LogInformation("Broadcasting batch of events");

        // Broadcast all events (parallel for better throughput)
        await Task.WhenAll(messages.Select(msg => 
            PublishAsync(msg, context, cancellationToken)));
    }

    // Helper methods
    private async Task SendToLeaderAsync<TMessage>(
        TMessage message,
        string destination,
        TransportContext? context,
        CancellationToken cancellationToken)
        where TMessage : class
    {
        var leaderId = _cluster.LeaderId;
        
        if (leaderId == null)
        {
            throw new InvalidOperationException("No leader elected in the cluster");
        }

        // Check if current node is the leader
        if (_cluster.IsLeader)
        {
            _logger.LogDebug("Handling command locally (current node is Leader)");
            await HandleLocallyAsync(message, destination, context, cancellationToken);
            return;
        }

        // Forward to leader
        _logger.LogInformation("Forwarding command to Leader: {LeaderId}", leaderId);
        
        // TODO: Implement actual HTTP/gRPC forwarding
        await Task.CompletedTask;
    }

    private Task HandleLocallyAsync<TMessage>(
        TMessage message,
        string destination,
        TransportContext? context,
        CancellationToken cancellationToken)
        where TMessage : class
    {
        _logger.LogDebug("Handling message locally: {MessageType}", typeof(TMessage).Name);
        
        // TODO: Implement local handling
        // This should integrate with the local mediator
        return Task.CompletedTask;
    }
}
