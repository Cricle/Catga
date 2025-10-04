using Catga.Messages;
using Catga.Results;

namespace Catga.StateMachine;

/// <summary>
/// 状态机接口
/// </summary>
public interface IStateMachine<TState, TData>
    where TState : struct, Enum
    where TData : class, new()
{
    /// <summary>
    /// 实例 ID
    /// </summary>
    Guid InstanceId { get; }

    /// <summary>
    /// 当前状态
    /// </summary>
    TState CurrentState { get; }

    /// <summary>
    /// 状态数据
    /// </summary>
    TData Data { get; }

    /// <summary>
    /// 触发事件
    /// </summary>
    Task<CatgaResult> FireAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent;

    /// <summary>
    /// 转换到指定状态
    /// </summary>
    Task<CatgaResult> TransitionToAsync(TState newState, CancellationToken cancellationToken = default);
}

