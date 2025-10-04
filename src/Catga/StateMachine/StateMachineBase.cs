using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.Logging;

namespace Catga.StateMachine;

/// <summary>
/// 状态机基类
/// </summary>
public abstract class StateMachineBase<TState, TData> : IStateMachine<TState, TData>
    where TState : struct, Enum
    where TData : class, new()
{
    private readonly Dictionary<(TState From, Type EventType), Func<IEvent, Task<TState?>>> _transitions = new();
    private readonly Dictionary<TState, List<Func<TData, Task>>> _onEnterActions = new();
    private readonly Dictionary<TState, List<Func<TData, Task>>> _onExitActions = new();
    private readonly ILogger _logger;

    protected StateMachineBase(ILogger logger)
    {
        _logger = logger;
        InstanceId = Guid.NewGuid();
        Data = new TData();
    }

    /// <inheritdoc/>
    public Guid InstanceId { get; protected set; }

    /// <inheritdoc/>
    public TState CurrentState { get; protected set; }

    /// <inheritdoc/>
    public TData Data { get; protected set; }

    /// <summary>
    /// 配置状态转换
    /// </summary>
    protected void ConfigureTransition<TEvent>(
        TState fromState,
        Func<TEvent, Task<TState?>> handler)
        where TEvent : IEvent
    {
        _transitions[(fromState, typeof(TEvent))] = async (@event) => await handler((TEvent)@event);
    }

    /// <summary>
    /// 配置进入状态时的动作
    /// </summary>
    protected void OnEnter(TState state, Func<TData, Task> action)
    {
        if (!_onEnterActions.ContainsKey(state))
        {
            _onEnterActions[state] = new List<Func<TData, Task>>();
        }
        _onEnterActions[state].Add(action);
    }

    /// <summary>
    /// 配置离开状态时的动作
    /// </summary>
    protected void OnExit(TState state, Func<TData, Task> action)
    {
        if (!_onExitActions.ContainsKey(state))
        {
            _onExitActions[state] = new List<Func<TData, Task>>();
        }
        _onExitActions[state].Add(action);
    }

    /// <inheritdoc/>
    public async Task<TransitResult> FireAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        var key = (CurrentState, typeof(TEvent));

        if (!_transitions.TryGetValue(key, out var handler))
        {
            _logger.LogWarning("No transition defined for state {State} and event {Event}",
                CurrentState, typeof(TEvent).Name);
            return TransitResult.Failure($"No transition from {CurrentState} on {typeof(TEvent).Name}");
        }

        try
        {
            var newState = await handler(@event);

            if (newState.HasValue)
            {
                return await TransitionToAsync(newState.Value, cancellationToken);
            }

            return TransitResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error firing event {Event} in state {State}",
                typeof(TEvent).Name, CurrentState);
            return TransitResult.Failure($"Transition failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<TransitResult> TransitionToAsync(TState newState, CancellationToken cancellationToken = default)
    {
        var oldState = CurrentState;

        try
        {
            // 执行离开当前状态的动作
            if (_onExitActions.TryGetValue(oldState, out var exitActions))
            {
                foreach (var action in exitActions)
                {
                    await action(Data);
                }
            }

            // 转换状态
            CurrentState = newState;

            _logger.LogInformation("State machine {InstanceId} transitioned from {OldState} to {NewState}",
                InstanceId, oldState, newState);

            // 执行进入新状态的动作
            if (_onEnterActions.TryGetValue(newState, out var enterActions))
            {
                foreach (var action in enterActions)
                {
                    await action(Data);
                }
            }

            return TransitResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transitioning from {OldState} to {NewState}", oldState, newState);
            return TransitResult.Failure($"Transition failed: {ex.Message}");
        }
    }
}

