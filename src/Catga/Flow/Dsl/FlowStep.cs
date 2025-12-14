using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Flow step metadata with strongly typed delegates.
/// </summary>
public class FlowStep<TState> where TState : class, IFlowState
{
    public StepType Type { get; set; }
    public bool HasResult { get; set; }
    public string? ResultPropertyName { get; set; }
    public bool HasCompensation { get; set; }
    public bool HasCondition { get; set; }
    public bool HasFailCondition { get; set; }
    public bool IsOptional { get; set; }
    public List<string> Tags { get; } = [];
    public TimeSpan? Timeout { get; set; }
    public int ChildRequestCount { get; set; }
    public bool HasOnCompletedHook { get; set; }
    public bool HasOnFailedHook { get; set; }

    // Execution delegates - strongly typed
    internal Func<TState, object>? RequestFactory { get; set; }
    internal Func<TState, IRequest>? CompensationFactory { get; set; }
    internal Func<TState, bool>? ConditionFactory { get; set; }
    internal Func<TState, object?, bool>? FailConditionFactory { get; set; }
    internal string? FailConditionMessage { get; set; }
    internal Action<TState, object?>? ResultSetter { get; set; }
    internal Func<TState, IEvent>? OnCompletedFactory { get; set; }
    internal Func<TState, string?, IEvent>? OnFailedFactory { get; set; }

    // WhenAll/WhenAny specific
    internal List<Func<TState, IRequest>>? ChildRequestFactories { get; set; }

    // Branching (If/Switch)
    internal Func<TState, bool>? BranchCondition { get; set; }
    public List<FlowStep<TState>>? ThenBranch { get; set; }
    public List<FlowStep<TState>>? ElseBranch { get; set; }
    public List<(Func<TState, bool> Condition, List<FlowStep<TState>> Steps)>? ElseIfBranches { get; set; }

    // Switch specific
    internal Func<TState, object>? SwitchSelector { get; set; }
    public Dictionary<object, List<FlowStep<TState>>>? Cases { get; set; }
    public List<FlowStep<TState>>? DefaultBranch { get; set; }

    // ForEach specific
    internal Func<TState, object>? CollectionSelector { get; set; }
    public List<FlowStep<TState>>? ItemSteps { get; set; }
    internal Action<object, FlowBuilder<TState>>? ItemStepsConfigurator { get; set; }
    public int BatchSize { get; set; } = 100;
    internal int? MaxDegreeOfParallelism { get; set; }
    internal bool StreamingEnabled { get; set; } = false;
    internal bool MetricsEnabled { get; set; } = false;
    internal bool CircuitBreakerEnabled { get; set; } = false;
    internal int CircuitBreakerFailureThreshold { get; set; } = 5;
    internal TimeSpan CircuitBreakerBreakDuration { get; set; } = TimeSpan.FromMinutes(1);
    public ForEachFailureHandling FailureHandling { get; set; } = ForEachFailureHandling.StopOnFirstFailure;
    internal Action<TState, object, object?>? OnItemSuccess { get; set; }
    internal Action<TState, object, string?>? OnItemFail { get; set; }
    internal Action<TState>? OnComplete { get; set; }
}

/// <summary>
/// Non-generic FlowStep for backward compatibility and storage.
/// Uses strongly typed delegates wrapped as object-based functions.
/// </summary>
public class FlowStep
{
    public StepType Type { get; set; }
    public bool HasResult { get; set; }
    public string? ResultPropertyName { get; set; }
    public bool HasCompensation { get; set; }
    public bool HasCondition { get; set; }
    public bool HasFailCondition { get; set; }
    public bool IsOptional { get; set; }
    public List<string> Tags { get; } = [];
    public TimeSpan? Timeout { get; set; }
    public int ChildRequestCount { get; set; }
    public bool HasOnCompletedHook { get; set; }
    public bool HasOnFailedHook { get; set; }

    // Execution delegates - stored as Delegate but called via typed wrappers
    internal Delegate? RequestFactory { get; set; }
    internal Delegate? CompensationFactory { get; set; }
    internal Delegate? ConditionFactory { get; set; }
    internal Delegate? FailConditionFactory { get; set; }
    internal string? FailConditionMessage { get; set; }
    internal Delegate? ResultSetter { get; set; }
    internal Delegate? OnCompletedFactory { get; set; }
    internal Delegate? OnFailedFactory { get; set; }

    // Strongly typed execution wrappers (set at build time)
    internal Func<object, object>? CreateRequest { get; set; }
    internal Func<object, IRequest>? CreateCompensation { get; set; }
    internal Func<object, bool>? EvaluateCondition { get; set; }
    internal Func<object, object?, bool>? EvaluateFailCondition { get; set; }
    internal Action<object, object?>? SetResult { get; set; }
    internal Func<object, IEvent>? CreateCompletedEvent { get; set; }
    internal Func<object, string?, IEvent>? CreateFailedEvent { get; set; }

    // AOT-compatible request executor (no reflection)
    internal Func<ICatgaMediator, object, CancellationToken, ValueTask<(bool Success, string? Error, object? Value)>>? ExecuteRequest { get; set; }

    // WhenAll/WhenAny specific
    internal List<Delegate>? ChildRequestFactories { get; set; }
    internal List<Func<object, IRequest>>? CreateChildRequests { get; set; }

    // Branching (If/Switch)
    internal Delegate? BranchCondition { get; set; }
    internal Func<object, bool>? EvaluateBranchCondition { get; set; }
    public List<FlowStep>? ThenBranch { get; set; }
    public List<FlowStep>? ElseBranch { get; set; }
    public List<(Delegate Condition, List<FlowStep> Steps)>? ElseIfBranches { get; set; }
    internal List<(Func<object, bool> Condition, List<FlowStep> Steps)>? TypedElseIfBranches { get; set; }

    // Switch specific
    internal Delegate? SwitchSelector { get; set; }
    internal Func<object, object>? EvaluateSwitchSelector { get; set; }
    public Dictionary<object, List<FlowStep>>? Cases { get; set; }
    public List<FlowStep>? DefaultBranch { get; set; }

    // ForEach specific
    internal Delegate? CollectionSelector { get; set; }
    internal Func<object, IEnumerable<object>>? GetCollection { get; set; }
    public List<FlowStep>? ItemSteps { get; set; }
    internal Delegate? ItemStepsConfigurator { get; set; }
    internal Action<object, object>? ConfigureItemSteps { get; set; }
    public int BatchSize { get; set; } = 100;
    internal int? MaxDegreeOfParallelism { get; set; }
    internal bool StreamingEnabled { get; set; } = false;
    internal bool MetricsEnabled { get; set; } = false;
    internal bool CircuitBreakerEnabled { get; set; } = false;
    internal int CircuitBreakerFailureThreshold { get; set; } = 5;
    internal TimeSpan CircuitBreakerBreakDuration { get; set; } = TimeSpan.FromMinutes(1);
    public ForEachFailureHandling FailureHandling { get; set; } = ForEachFailureHandling.StopOnFirstFailure;
    internal Delegate? OnItemSuccess { get; set; }
    internal Delegate? OnItemFail { get; set; }
    internal Delegate? OnComplete { get; set; }
    internal Action<object, object, object?>? InvokeItemSuccess { get; set; }
    internal Action<object, object, string?>? InvokeItemFail { get; set; }
    internal Action<object>? InvokeComplete { get; set; }
}

/// <summary>
/// Step type.
/// </summary>
public enum StepType
{
    Send,
    Query,
    Publish,
    WhenAll,
    WhenAny,
    If,
    Switch,
    ForEach
}
