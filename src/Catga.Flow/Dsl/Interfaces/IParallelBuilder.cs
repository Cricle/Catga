namespace Catga.Flow.Dsl;

/// <summary>
/// Builder for Parallel step - executes multiple independent step branches concurrently.
/// </summary>
public interface IParallelBuilder<TState> where TState : class, IFlowState
{
    /// <summary>Add a parallel branch with its own step sequence.</summary>
    IParallelBuilder<TState> Branch(Action<IFlowBuilder<TState>> configure);

    /// <summary>Wait for all branches to complete (default). Set false to complete on first success.</summary>
    IParallelBuilder<TState> WaitAll(bool waitAll = true);

    /// <summary>Set timeout for all parallel branches.</summary>
    IParallelBuilder<TState> Timeout(TimeSpan timeout);

    /// <summary>End parallel configuration and return to main flow.</summary>
    IFlowBuilder<TState> EndParallel();
}

/// <summary>
/// Builder for Throttle step - limits concurrent execution.
/// </summary>
public interface IThrottleBuilder<TState> where TState : class, IFlowState
{
    /// <summary>Configure steps to execute under throttle.</summary>
    IThrottleBuilder<TState> Execute(Action<IFlowBuilder<TState>> configure);

    /// <summary>End throttle configuration.</summary>
    IFlowBuilder<TState> EndThrottle();
}
