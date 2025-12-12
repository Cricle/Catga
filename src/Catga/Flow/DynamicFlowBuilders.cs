using System.Linq.Expressions;
using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Dynamic flow builder for runtime step generation.
/// </summary>
public interface IDynamicFlowBuilder<TState> where TState : class, IFlowState
{
    /// <summary>
    /// Dynamically generate steps at runtime.
    /// </summary>
    IDynamicFlowBuilder<TState> Dynamic(Func<TState, IEnumerable<FlowStep>> stepGenerator);

    /// <summary>
    /// Conditionally include steps if a value is present.
    /// </summary>
    IDynamicFlowBuilder<TState> IfPresent<TValue>(
        Expression<Func<TState, TValue?>> selector,
        Action<IFlowBuilder<TState>, TValue> configure)
        where TValue : class;

    /// <summary>
    /// Conditionally include steps if a value is not null.
    /// </summary>
    IDynamicFlowBuilder<TState> IfNotNull<TValue>(
        Expression<Func<TState, TValue?>> selector,
        Action<IFlowBuilder<TState>, TValue> configure);

    /// <summary>
    /// Execute steps for each item in a collection with dynamic configuration.
    /// </summary>
    IDynamicFlowBuilder<TState> ForEachDynamic<TItem>(
        Expression<Func<TState, IEnumerable<TItem>>> collectionSelector,
        Func<TItem, IEnumerable<FlowStep>> stepGenerator);

    /// <summary>
    /// End the dynamic flow builder.
    /// </summary>
    IFlowBuilder<TState> End();
}

/// <summary>
/// Recursive flow builder for calling other flows.
/// </summary>
public interface IRecursiveFlowBuilder<TState> where TState : class, IFlowState
{
    /// <summary>
    /// Call another flow with state mapping.
    /// </summary>
    IRecursiveFlowBuilder<TState> CallFlow<TOtherFlow>(
        Expression<Func<TState, IFlowState>> stateMapper)
        where TOtherFlow : FlowConfig<IFlowState>;

    /// <summary>
    /// Call another flow with state mapping and result handling.
    /// </summary>
    IRecursiveFlowBuilder<TState> CallFlow<TOtherFlow, TResult>(
        Expression<Func<TState, IFlowState>> stateMapper,
        Expression<Func<TState, TResult, TState>> resultMerger)
        where TOtherFlow : FlowConfig<IFlowState>;

    /// <summary>
    /// Recursively call the current flow with condition.
    /// </summary>
    IRecursiveFlowBuilder<TState> RecursiveCall(
        Expression<Func<TState, bool>> shouldContinue,
        Action<IFlowBuilder<TState>> configure);

    /// <summary>
    /// Set maximum recursion depth.
    /// </summary>
    IRecursiveFlowBuilder<TState> MaxDepth(int depth);

    /// <summary>
    /// End the recursive flow builder.
    /// </summary>
    IFlowBuilder<TState> End();
}

/// <summary>
/// Dynamic step information.
/// </summary>
internal class DynamicStepInfo<TState> where TState : class, IFlowState
{
    public Func<TState, IEnumerable<FlowStep>>? StepGenerator { get; set; }
    public Type? ValueType { get; set; }
    public Expression? ValueSelector { get; set; }
    public Delegate? ConfigureAction { get; set; }
    public bool CheckForNull { get; set; }
}

/// <summary>
/// Recursive call information.
/// </summary>
internal class RecursiveCallInfo<TState> where TState : class, IFlowState
{
    public Type? FlowType { get; set; }
    public Expression<Func<TState, IFlowState>>? StateMapper { get; set; }
    public Delegate? ResultMerger { get; set; }
    public Expression<Func<TState, bool>>? Condition { get; set; }
    public Action<IFlowBuilder<TState>>? Configure { get; set; }
    public int MaxDepth { get; set; } = 10;
}

/// <summary>
/// Implementation of dynamic flow builder.
/// </summary>
internal class DynamicFlowBuilder<TState> : IDynamicFlowBuilder<TState> where TState : class, IFlowState
{
    private readonly FlowBuilder<TState> _flowBuilder;
    private readonly List<DynamicStepInfo<TState>> _dynamicSteps = new();

    public DynamicFlowBuilder(FlowBuilder<TState> flowBuilder)
    {
        _flowBuilder = flowBuilder;
    }

    public IDynamicFlowBuilder<TState> Dynamic(Func<TState, IEnumerable<FlowStep>> stepGenerator)
    {
        _dynamicSteps.Add(new DynamicStepInfo<TState>
        {
            StepGenerator = stepGenerator
        });
        return this;
    }

    public IDynamicFlowBuilder<TState> IfPresent<TValue>(
        Expression<Func<TState, TValue?>> selector,
        Action<IFlowBuilder<TState>, TValue> configure)
        where TValue : class
    {
        _dynamicSteps.Add(new DynamicStepInfo<TState>
        {
            ValueType = typeof(TValue),
            ValueSelector = selector,
            ConfigureAction = configure,
            CheckForNull = true
        });
        return this;
    }

    public IDynamicFlowBuilder<TState> IfNotNull<TValue>(
        Expression<Func<TState, TValue?>> selector,
        Action<IFlowBuilder<TState>, TValue> configure)
    {
        _dynamicSteps.Add(new DynamicStepInfo<TState>
        {
            ValueType = typeof(TValue),
            ValueSelector = selector,
            ConfigureAction = configure,
            CheckForNull = true
        });
        return this;
    }

    public IDynamicFlowBuilder<TState> ForEachDynamic<TItem>(
        Expression<Func<TState, IEnumerable<TItem>>> collectionSelector,
        Func<TItem, IEnumerable<FlowStep>> stepGenerator)
    {
        // Implementation would handle dynamic step generation for each item
        return this;
    }

    public IFlowBuilder<TState> End()
    {
        // Apply all dynamic steps to the flow builder
        foreach (var step in _dynamicSteps)
        {
            ApplyDynamicStep(step);
        }
        return _flowBuilder;
    }

    private void ApplyDynamicStep(DynamicStepInfo<TState> step)
    {
        if (step.StepGenerator != null)
        {
            // Dynamic step generation would be handled in executor
        }
        else if (step.ValueSelector != null)
        {
            // IfPresent/IfNotNull would be handled in executor
        }
    }
}

/// <summary>
/// Implementation of recursive flow builder.
/// </summary>
internal class RecursiveFlowBuilder<TState> : IRecursiveFlowBuilder<TState> where TState : class, IFlowState
{
    private readonly FlowBuilder<TState> _flowBuilder;
    private readonly List<RecursiveCallInfo<TState>> _recursiveCalls = new();

    public RecursiveFlowBuilder(FlowBuilder<TState> flowBuilder)
    {
        _flowBuilder = flowBuilder;
    }

    public IRecursiveFlowBuilder<TState> CallFlow<TOtherFlow>(
        Expression<Func<TState, IFlowState>> stateMapper)
        where TOtherFlow : FlowConfig<IFlowState>
    {
        _recursiveCalls.Add(new RecursiveCallInfo<TState>
        {
            FlowType = typeof(TOtherFlow),
            StateMapper = stateMapper
        });
        return this;
    }

    public IRecursiveFlowBuilder<TState> CallFlow<TOtherFlow, TResult>(
        Expression<Func<TState, IFlowState>> stateMapper,
        Expression<Func<TState, TResult, TState>> resultMerger)
        where TOtherFlow : FlowConfig<IFlowState>
    {
        _recursiveCalls.Add(new RecursiveCallInfo<TState>
        {
            FlowType = typeof(TOtherFlow),
            StateMapper = stateMapper,
            ResultMerger = resultMerger.Compile()
        });
        return this;
    }

    public IRecursiveFlowBuilder<TState> RecursiveCall(
        Expression<Func<TState, bool>> shouldContinue,
        Action<IFlowBuilder<TState>> configure)
    {
        _recursiveCalls.Add(new RecursiveCallInfo<TState>
        {
            Condition = shouldContinue,
            Configure = configure
        });
        return this;
    }

    public IRecursiveFlowBuilder<TState> MaxDepth(int depth)
    {
        if (_recursiveCalls.Count > 0)
        {
            _recursiveCalls[_recursiveCalls.Count - 1].MaxDepth = depth;
        }
        return this;
    }

    public IFlowBuilder<TState> End()
    {
        // Apply all recursive calls to the flow builder
        foreach (var call in _recursiveCalls)
        {
            ApplyRecursiveCall(call);
        }
        return _flowBuilder;
    }

    private void ApplyRecursiveCall(RecursiveCallInfo<TState> call)
    {
        // Recursive call handling would be implemented in executor
    }
}

/// <summary>
/// Extension methods for dynamic and recursive flow building.
/// </summary>
public static class DynamicFlowExtensions
{
    /// <summary>
    /// Start building dynamic steps.
    /// </summary>
    public static IDynamicFlowBuilder<TState> Dynamic<TState>(this IFlowBuilder<TState> builder)
        where TState : class, IFlowState
    {
        if (builder is FlowBuilder<TState> flowBuilder)
        {
            return new DynamicFlowBuilder<TState>(flowBuilder);
        }
        throw new InvalidOperationException("Invalid flow builder type");
    }

    /// <summary>
    /// Start building recursive calls.
    /// </summary>
    public static IRecursiveFlowBuilder<TState> Recursive<TState>(this IFlowBuilder<TState> builder)
        where TState : class, IFlowState
    {
        if (builder is FlowBuilder<TState> flowBuilder)
        {
            return new RecursiveFlowBuilder<TState>(flowBuilder);
        }
        throw new InvalidOperationException("Invalid flow builder type");
    }
}
