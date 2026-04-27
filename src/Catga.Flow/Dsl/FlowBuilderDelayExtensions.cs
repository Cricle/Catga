using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Extension methods for Delay and ScheduleAt steps in Flow DSL.
/// </summary>
public static class FlowBuilderDelayExtensions
{
    /// <summary>
    /// Add a delay step that suspends the flow for a specified duration.
    /// The flow will be scheduled to resume after the delay.
    /// </summary>
    public static IFlowBuilder<TState> Delay<TState>(
        this IFlowBuilder<TState> builder,
        TimeSpan delay)
        where TState : class, IFlowState
    {
        var flowBuilder = (FlowBuilder<TState>)builder;
        var step = new FlowStep
        {
            Type = StepType.Delay,
            DelayDuration = delay
        };
        flowBuilder.Steps.Add(step);
        return builder;
    }

    /// <summary>
    /// Add a schedule step that suspends the flow until a specific time.
    /// The time is computed from the current state.
    /// </summary>
    public static IFlowBuilder<TState> ScheduleAt<TState>(
        this IFlowBuilder<TState> builder,
        Func<TState, DateTime> timeSelector)
        where TState : class, IFlowState
    {
        var flowBuilder = (FlowBuilder<TState>)builder;
        var step = new FlowStep
        {
            Type = StepType.ScheduleAt,
            ScheduleTimeSelector = timeSelector,
            GetScheduleTime = state => timeSelector((TState)state)
        };
        flowBuilder.Steps.Add(step);
        return builder;
    }

}
