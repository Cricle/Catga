using Catga.Abstractions;
using System.Linq.Expressions;
using System.Reflection;

namespace Catga.Flow.Dsl;

/// <summary>
/// ForEach builder interface.
/// </summary>
public interface IForEachBuilder<TState, TItem> where TState : class, IFlowState
{
    /// <summary>Configure steps to execute for each item.</summary>
    IForEachBuilder<TState, TItem> Configure(Action<TItem, IFlowBuilder<TState>> configureSteps);

    /// <summary>Set batch size for processing items.</summary>
    IForEachBuilder<TState, TItem> WithBatchSize(int batchSize);

    /// <summary>Enable streaming mode for large or infinite collections.</summary>
    IForEachBuilder<TState, TItem> WithStreaming(bool enabled = true);

    /// <summary>Enable performance metrics collection.</summary>
    IForEachBuilder<TState, TItem> WithMetrics(bool enabled = true);

    /// <summary>Configure circuit breaker for failure resilience.</summary>
    IForEachBuilder<TState, TItem> WithCircuitBreaker(int failureThreshold = 5, TimeSpan breakDuration = default);

    /// <summary>Set maximum degree of parallelism for processing items.</summary>
    IForEachBuilder<TState, TItem> WithParallelism(int maxDegreeOfParallelism);

    /// <summary>Continue processing on item failure.</summary>
    IForEachBuilder<TState, TItem> ContinueOnFailure();

    /// <summary>Stop processing on first item failure.</summary>
    IForEachBuilder<TState, TItem> StopOnFirstFailure();

    /// <summary>Set callback for item success.</summary>
    IForEachBuilder<TState, TItem> OnItemSuccess(Action<TState, TItem, object> callback);

    /// <summary>Set callback for item failure.</summary>
    IForEachBuilder<TState, TItem> OnItemFail(Action<TState, TItem, string> callback);

    /// <summary>Set callback for completion.</summary>
    IForEachBuilder<TState, TItem> OnComplete(Action<TState> callback);

    /// <summary>End ForEach configuration.</summary>
    IFlowBuilder<TState> EndForEach();
}

/// <summary>
/// ForEach builder implementation.
/// </summary>
internal class ForEachBuilder<TState, TItem> : IForEachBuilder<TState, TItem> where TState : class, IFlowState
{
    private readonly FlowBuilder<TState> _flowBuilder;
    private readonly FlowStep _forEachStep;

    public ForEachBuilder(FlowBuilder<TState> flowBuilder, FlowStep forEachStep)
    {
        _flowBuilder = flowBuilder;
        _forEachStep = forEachStep;
    }

    public IForEachBuilder<TState, TItem> Configure(Action<TItem, IFlowBuilder<TState>> configureSteps)
    {
        // Store the configuration delegate for runtime execution
        _forEachStep.ItemStepsConfigurator = configureSteps;

        // For now, create empty item steps to indicate configuration was called
        _forEachStep.ItemSteps = [];

        return this;
    }

    public IForEachBuilder<TState, TItem> WithBatchSize(int batchSize)
    {
        _forEachStep.BatchSize = batchSize;
        return this;
    }

    public IForEachBuilder<TState, TItem> WithStreaming(bool enabled = true)
    {
        _forEachStep.StreamingEnabled = enabled;
        return this;
    }

    public IForEachBuilder<TState, TItem> WithMetrics(bool enabled = true)
    {
        _forEachStep.MetricsEnabled = enabled;
        return this;
    }

    public IForEachBuilder<TState, TItem> WithCircuitBreaker(int failureThreshold = 5, TimeSpan breakDuration = default)
    {
        _forEachStep.CircuitBreakerEnabled = true;
        _forEachStep.CircuitBreakerFailureThreshold = failureThreshold;
        _forEachStep.CircuitBreakerBreakDuration = breakDuration == default ? TimeSpan.FromMinutes(1) : breakDuration;
        return this;
    }

    public IForEachBuilder<TState, TItem> WithParallelism(int maxDegreeOfParallelism)
    {
        _forEachStep.MaxDegreeOfParallelism = maxDegreeOfParallelism;
        return this;
    }

    public IForEachBuilder<TState, TItem> ContinueOnFailure()
    {
        _forEachStep.FailureHandling = ForEachFailureHandling.ContinueOnFailure;
        return this;
    }

    public IForEachBuilder<TState, TItem> StopOnFirstFailure()
    {
        _forEachStep.FailureHandling = ForEachFailureHandling.StopOnFirstFailure;
        return this;
    }

    public IForEachBuilder<TState, TItem> OnItemSuccess(Action<TState, TItem, object> callback)
    {
        _forEachStep.OnItemSuccess = callback;
        return this;
    }

    public IForEachBuilder<TState, TItem> OnItemFail(Action<TState, TItem, string> callback)
    {
        _forEachStep.OnItemFail = callback;
        return this;
    }

    public IForEachBuilder<TState, TItem> OnComplete(Action<TState> callback)
    {
        _forEachStep.OnComplete = callback;
        return this;
    }

    public IFlowBuilder<TState> EndForEach()
    {
        return _flowBuilder;
    }
}

