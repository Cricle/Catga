using System.Diagnostics.CodeAnalysis;
using Catga.Messages;
using Catga.Results;

namespace Catga.Distributed;

/// <summary>
/// 分布式 Mediator（扩展 ICatgaMediator 支持分布式）
/// </summary>
public interface IDistributedMediator : ICatgaMediator
{
    /// <summary>
    /// 获取所有在线节点
    /// </summary>
    Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取当前节点信息
    /// </summary>
    Task<NodeInfo> GetCurrentNodeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送消息到指定节点
    /// </summary>
    [RequiresDynamicCode("Distributed mediator uses reflection for message routing and serialization")]
    [RequiresUnreferencedCode("Distributed mediator may require types that cannot be statically analyzed")]
    Task<CatgaResult<TResponse>> SendToNodeAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest, 
        TResponse>(
        TRequest request,
        string nodeId,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>;

    /// <summary>
    /// 广播消息到所有节点
    /// </summary>
    [RequiresDynamicCode("Distributed mediator uses reflection for message routing and serialization")]
    [RequiresUnreferencedCode("Distributed mediator may require types that cannot be statically analyzed")]
    Task BroadcastAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent;
}

/// <summary>
/// 节点信息
/// </summary>
public record NodeInfo
{
    public required string NodeId { get; init; }
    public required string Endpoint { get; init; }
    public DateTime LastSeen { get; init; } = DateTime.UtcNow;
    public double Load { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
    public bool IsOnline => (DateTime.UtcNow - LastSeen).TotalSeconds < 30;
}

