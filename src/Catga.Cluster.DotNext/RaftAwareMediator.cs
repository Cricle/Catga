using Catga.Handlers;
using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.Logging;

namespace Catga.Cluster.DotNext;

/// <summary>
/// Raft-aware mediator that automatically routes commands to leader
/// </summary>
public class RaftAwareMediator : ICatgaMediator
{
    private readonly ICatgaRaftCluster _cluster;
    private readonly ICatgaMediator _localMediator;
    private readonly ILogger<RaftAwareMediator> _logger;

    public RaftAwareMediator(
        ICatgaRaftCluster cluster,
        ICatgaMediator localMediator,
        ILogger<RaftAwareMediator> logger)
    {
        _cluster = cluster;
        _localMediator = localMediator;
        _logger = logger;
    }

    /// <summary>
    /// Send request with automatic routing
    /// - Command: Routes to Leader (write operation)
    /// - Query: Executes locally (read operation)
    /// </summary>
    public async ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(request);

        // Check if current node is the leader
        var isLeader = _cluster.IsLeader;

        // For Commands (write operations), must go through Leader
        // For Queries (read operations), can execute locally
        bool isCommand = IsWriteOperation<TRequest>();

        if (isCommand && !isLeader)
        {
            _logger.LogInformation(
                "Forwarding command {RequestType} to Leader {LeaderId}",
                typeof(TRequest).Name,
                _cluster.LeaderId ?? "unknown");

            // Forward to leader
            return await ForwardToLeaderAsync<TRequest, TResponse>(request, cancellationToken);
        }

        // Execute locally
        _logger.LogDebug(
            "Executing {RequestType} locally (isLeader: {IsLeader})",
            typeof(TRequest).Name,
            isLeader);

        return await _localMediator.SendAsync<TRequest, TResponse>(request, cancellationToken);
    }

    /// <summary>
    /// Send request without response
    /// </summary>
    public async Task<CatgaResult> SendAsync<TRequest>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        ArgumentNullException.ThrowIfNull(request);

        // Check if current node is the leader
        var isLeader = _cluster.IsLeader;

        if (!isLeader)
        {
            _logger.LogInformation(
                "Forwarding command {RequestType} to Leader {LeaderId}",
                typeof(TRequest).Name,
                _cluster.LeaderId ?? "unknown");

            // TODO: Forward to leader
            return CatgaResult.Failure("Leader forwarding not yet implemented");
        }

        return await _localMediator.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Publish event (broadcasts to all nodes)
    /// </summary>
    public async Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(@event);

        _logger.LogInformation(
            "Broadcasting event {EventType} to all cluster nodes",
            typeof(TEvent).Name);

        // Publish locally first
        await _localMediator.PublishAsync(@event, cancellationToken);

        // TODO: Broadcast to other nodes
        // For now, rely on local mediator
    }

    /// <summary>
    /// Send batch requests
    /// </summary>
    public async ValueTask<IReadOnlyList<CatgaResult<TResponse>>> SendBatchAsync<TRequest, TResponse>(
        IReadOnlyList<TRequest> requests,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(requests);

        _logger.LogInformation(
            "Processing batch of {Count} {RequestType} requests",
            requests.Count,
            typeof(TRequest).Name);

        // Process all requests (with automatic routing)
        var tasks = new List<ValueTask<CatgaResult<TResponse>>>(requests.Count);
        foreach (var request in requests)
        {
            tasks.Add(SendAsync<TRequest, TResponse>(request, cancellationToken));
        }

        var results = new List<CatgaResult<TResponse>>(requests.Count);
        foreach (var task in tasks)
        {
            results.Add(await task);
        }

        return results;
    }

    /// <summary>
    /// Send requests as a stream
    /// </summary>
    public async IAsyncEnumerable<CatgaResult<TResponse>> SendStreamAsync<TRequest, TResponse>(
        IAsyncEnumerable<TRequest> requests,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(requests);

        await foreach (var request in requests.WithCancellation(cancellationToken))
        {
            yield return await SendAsync<TRequest, TResponse>(request, cancellationToken);
        }
    }

    /// <summary>
    /// Publish batch events
    /// </summary>
    public async Task PublishBatchAsync<TEvent>(
        IReadOnlyList<TEvent> events,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(events);

        _logger.LogInformation(
            "Broadcasting batch of {Count} {EventType} events",
            events.Count,
            typeof(TEvent).Name);

        foreach (var @event in events)
        {
            await PublishAsync(@event, cancellationToken);
        }
    }

    // Helper methods
    private static bool IsWriteOperation<TRequest>()
    {
        // Heuristic: Request types containing "Command", "Create", "Update", "Delete" are write operations
        var typeName = typeof(TRequest).Name;
        return typeName.Contains("Command", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("Create", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("Delete", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("Set", StringComparison.OrdinalIgnoreCase);
    }

    private async ValueTask<CatgaResult<TResponse>> ForwardToLeaderAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        var leaderId = _cluster.LeaderId;
        
        if (leaderId == null)
        {
            _logger.LogWarning("No leader available in cluster");
            return CatgaResult<TResponse>.Failure("No leader elected in the cluster");
        }

        // TODO: Implement actual HTTP/gRPC forwarding to leader
        // For now, return error
        _logger.LogWarning("Leader forwarding not yet fully implemented");
        return CatgaResult<TResponse>.Failure($"Forwarding to leader {leaderId} not yet implemented");
    }
}

