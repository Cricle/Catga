using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Catga.Pipeline;

namespace Catga.Cluster;

/// <summary>
/// Pipeline behavior that ensures commands are only executed on the leader node.
/// Non-leader nodes will return a failure result.
/// </summary>
public sealed class LeaderOnlyBehavior<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IClusterCoordinator _coordinator;

    public LeaderOnlyBehavior(IClusterCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    public ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        if (!_coordinator.IsLeader)
        {
            return ValueTask.FromResult(CatgaResult<TResponse>.Failure(
                $"This node is not the leader. Leader: {_coordinator.LeaderEndpoint ?? "unknown"}"));
        }

        return next();
    }
}

/// <summary>
/// Marker interface for commands that should only execute on leader.
/// </summary>
public interface ILeaderOnlyCommand { }

/// <summary>
/// Pipeline behavior that forwards commands to leader if not on leader node.
/// Requires IClusterForwarder implementation.
/// </summary>
public sealed class ForwardToLeaderBehavior<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IClusterCoordinator _coordinator;
    private readonly IClusterForwarder? _forwarder;

    public ForwardToLeaderBehavior(IClusterCoordinator coordinator, IClusterForwarder? forwarder = null)
    {
        _coordinator = coordinator;
        _forwarder = forwarder;
    }

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        if (_coordinator.IsLeader)
        {
            return await next();
        }

        if (_forwarder == null || _coordinator.LeaderEndpoint == null)
        {
            return CatgaResult<TResponse>.Failure("Not leader and no forwarder available");
        }

        return await _forwarder.ForwardAsync<TRequest, TResponse>(
            request,
            _coordinator.LeaderEndpoint,
            cancellationToken);
    }
}

/// <summary>
/// Interface for forwarding requests to leader node.
/// Implement this to enable automatic request forwarding.
/// </summary>
public interface IClusterForwarder
{
    Task<CatgaResult<TResponse>> ForwardAsync<TRequest, TResponse>(
        TRequest request,
        string leaderEndpoint,
        CancellationToken ct = default)
        where TRequest : IRequest<TResponse>;
}
