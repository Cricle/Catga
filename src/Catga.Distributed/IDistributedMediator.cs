using System.Diagnostics.CodeAnalysis;
using Catga.Messages;
using Catga.Results;

namespace Catga.Distributed;

/// <summary>Distributed Mediator (extends ICatgaMediator)</summary>
public interface IDistributedMediator : ICatgaMediator
{
    public Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken cancellationToken = default);
    public Task<NodeInfo> GetCurrentNodeAsync(CancellationToken cancellationToken = default);
    public Task<CatgaResult<TResponse>> SendToNodeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(TRequest request, string nodeId, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>;
    public Task BroadcastAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent;
}

/// <summary>Node information</summary>
public record NodeInfo
{
    public required string NodeId { get; init; }
    public required string Endpoint { get; init; }
    public DateTime LastSeen { get; init; } = DateTime.UtcNow;
    public double Load { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
    public bool IsOnline => (DateTime.UtcNow - LastSeen).TotalSeconds < 30;
}

