namespace Catga.Flow.Dsl;

internal sealed class ParallelBuilder<TState> : IParallelBuilder<TState>
    where TState : class, IFlowState
{
    private readonly FlowBuilder<TState> _parent;
    private readonly FlowStep _step;

    public ParallelBuilder(FlowBuilder<TState> parent, FlowStep step)
    {
        _parent = parent;
        _step = step;
        _step.ParallelBranches = [];
    }

    public IParallelBuilder<TState> Branch(Action<IFlowBuilder<TState>> configure)
    {
        var branchBuilder = new FlowBuilder<TState>();
        configure(branchBuilder);
        _step.ParallelBranches!.Add(branchBuilder.Steps);
        return this;
    }

    public IParallelBuilder<TState> WaitAll(bool waitAll = true)
    {
        _step.ParallelWaitAll = waitAll;
        return this;
    }

    public IParallelBuilder<TState> Timeout(TimeSpan timeout)
    {
        _step.Timeout = timeout;
        return this;
    }

    public IFlowBuilder<TState> EndParallel() => _parent;
}

internal sealed class ThrottleBuilder<TState> : IThrottleBuilder<TState>
    where TState : class, IFlowState
{
    private readonly FlowBuilder<TState> _parent;
    private readonly FlowStep _step;

    public ThrottleBuilder(FlowBuilder<TState> parent, FlowStep step)
    {
        _parent = parent;
        _step = step;
        _step.ThrottleSteps = [];
    }

    public IThrottleBuilder<TState> Execute(Action<IFlowBuilder<TState>> configure)
    {
        var innerBuilder = new FlowBuilder<TState>();
        configure(innerBuilder);
        _step.ThrottleSteps = innerBuilder.Steps;
        return this;
    }

    public IFlowBuilder<TState> EndThrottle() => _parent;
}
