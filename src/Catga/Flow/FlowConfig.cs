using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Base class for flow configuration.
/// </summary>
public abstract class FlowConfig<TState> where TState : class, IFlowState
{
    private readonly FlowBuilder<TState> _builder = new();
    private bool _built;

    /// <summary>Flow name.</summary>
    public string Name => _builder.FlowName ?? GetType().Name;

    /// <summary>Flow steps.</summary>
    public IReadOnlyList<FlowStep> Steps => _builder.Steps;

    /// <summary>Default timeout.</summary>
    public TimeSpan DefaultTimeout => _builder.DefaultTimeout;

    /// <summary>Has OnFlowCompleted hook.</summary>
    public bool HasOnFlowCompletedHook => _builder.OnFlowCompletedFactory != null;

    /// <summary>Has OnFlowFailed hook.</summary>
    public bool HasOnFlowFailedHook => _builder.OnFlowFailedFactory != null;

    /// <summary>Has OnStepCompleted hook.</summary>
    public bool HasOnStepCompletedHook => _builder.OnStepCompletedFactory != null;

    /// <summary>Has OnStepFailed hook.</summary>
    public bool HasOnStepFailedHook => _builder.OnStepFailedFactory != null;

    /// <summary>OnFlowCompleted factory.</summary>
    internal Func<TState, IEvent>? OnFlowCompletedFactory => _builder.OnFlowCompletedFactory;

    /// <summary>OnFlowFailed factory.</summary>
    internal Func<TState, string?, IEvent>? OnFlowFailedFactory => _builder.OnFlowFailedFactory;

    /// <summary>OnStepCompleted factory.</summary>
    internal Func<TState, int, IEvent>? OnStepCompletedFactory => _builder.OnStepCompletedFactory;

    /// <summary>OnStepFailed factory.</summary>
    internal Func<TState, int, string?, IEvent>? OnStepFailedFactory => _builder.OnStepFailedFactory;

    /// <summary>Build the flow configuration.</summary>
    public void Build()
    {
        if (_built) return;
        Configure(_builder);
        _built = true;
    }

    /// <summary>Configure the flow using DSL.</summary>
    protected abstract void Configure(IFlowBuilder<TState> flow);

    /// <summary>Get timeout for tag.</summary>
    public TimeSpan GetTimeoutForTag(string tag)
        => _builder.TaggedTimeouts.TryGetValue(tag, out var timeout) ? timeout : DefaultTimeout;

    /// <summary>Get retry count for tag.</summary>
    public int GetRetryForTag(string tag)
        => _builder.TaggedRetries.TryGetValue(tag, out var retry) ? retry : _builder.DefaultRetries;

    /// <summary>Check if should persist for tag.</summary>
    public bool ShouldPersistForTag(string tag)
        => _builder.TaggedPersist.Contains(tag);
}
