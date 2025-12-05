using System.Linq.Expressions;
using Catga.Abstractions;

namespace Catga.Flow.Dsl;

#region Flow Config Base

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

#endregion

#region Flow Builder

/// <summary>
/// Flow builder interface.
/// </summary>
public interface IFlowBuilder<TState> where TState : class, IFlowState
{
    // Name
    IFlowBuilder<TState> Name(string name);

    // Global settings
    ITaggedSetting Timeout(TimeSpan timeout);
    ITaggedSetting Retry(int maxRetries);
    ITaggedSetting Persist();

    // Event hooks
    IFlowBuilder<TState> OnStepCompleted<TEvent>(Func<TState, int, TEvent> factory) where TEvent : IEvent;
    IFlowBuilder<TState> OnStepFailed<TEvent>(Func<TState, int, string?, TEvent> factory) where TEvent : IEvent;
    IFlowBuilder<TState> OnFlowCompleted<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent;
    IFlowBuilder<TState> OnFlowFailed<TEvent>(Func<TState, string?, TEvent> factory) where TEvent : IEvent;

    // Steps
    IStepBuilder<TState> Send<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest;
    IStepBuilder<TState, TResult> Send<TResult>(Func<TState, IRequest<TResult>> factory);
    IQueryBuilder<TState, TResult> Query<TResult>(Func<TState, IRequest<TResult>> factory);
    IPublishBuilder<TState> Publish<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent;

    // Parallel
    IWhenAllBuilder<TState> WhenAll(params Func<TState, IRequest>[] requests);
    IWhenAnyBuilder<TState> WhenAny(params Func<TState, IRequest>[] requests);
    IWhenAnyBuilder<TState, TResult> WhenAny<TResult>(params Func<TState, IRequest<TResult>>[] requests);
}

/// <summary>
/// Tagged setting for ForTags().
/// </summary>
public interface ITaggedSetting
{
    void ForTags(params string[] tags);
}

/// <summary>
/// Step builder interface.
/// </summary>
public interface IStepBuilder<TState> where TState : class, IFlowState
{
    IStepBuilder<TState> IfFail<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest;
    IStepBuilder<TState> FailIf(Func<TState, bool> condition);
    IStepBuilder<TState> FailIf(Func<TState, bool> condition, string errorMessage);
    IStepBuilder<TState> OnlyWhen(Func<TState, bool> condition);
    IStepBuilder<TState> Optional();
    IStepBuilder<TState> Tag(params string[] tags);
    IStepBuilder<TState> OnCompleted<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent;
    IStepBuilder<TState> OnFailed<TEvent>(Func<TState, string?, TEvent> factory) where TEvent : IEvent;
}

/// <summary>
/// Step builder with result.
/// </summary>
public interface IStepBuilder<TState, TResult> : IStepBuilder<TState> where TState : class, IFlowState
{
    IStepBuilder<TState> Into(Expression<Func<TState, TResult>> property);
    new IStepBuilder<TState, TResult> IfFail<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest;
    IStepBuilder<TState, TResult> FailIf(Func<TResult, bool> condition);
    IStepBuilder<TState, TResult> FailIf(Func<TResult, bool> condition, string errorMessage);
}

/// <summary>
/// Query builder interface.
/// </summary>
public interface IQueryBuilder<TState, TResult> where TState : class, IFlowState
{
    IStepBuilder<TState> Into(Expression<Func<TState, TResult>> property);
    IQueryBuilder<TState, TResult> Tag(params string[] tags);
}

/// <summary>
/// Publish builder interface.
/// </summary>
public interface IPublishBuilder<TState> where TState : class, IFlowState
{
    IPublishBuilder<TState> Tag(params string[] tags);
}

/// <summary>
/// WhenAll builder interface.
/// </summary>
public interface IWhenAllBuilder<TState> where TState : class, IFlowState
{
    IWhenAllBuilder<TState> Timeout(TimeSpan timeout);
    IWhenAllBuilder<TState> IfAnyFail<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest;
    IWhenAllBuilder<TState> Tag(params string[] tags);
}

/// <summary>
/// WhenAny builder interface.
/// </summary>
public interface IWhenAnyBuilder<TState> where TState : class, IFlowState
{
    IWhenAnyBuilder<TState> Timeout(TimeSpan timeout);
    IWhenAnyBuilder<TState> Tag(params string[] tags);
}

/// <summary>
/// WhenAny builder with result.
/// </summary>
public interface IWhenAnyBuilder<TState, TResult> where TState : class, IFlowState
{
    IStepBuilder<TState> Into(Expression<Func<TState, TResult>> property);
    IWhenAnyBuilder<TState, TResult> Timeout(TimeSpan timeout);
    IWhenAnyBuilder<TState, TResult> Tag(params string[] tags);
}

#endregion

#region Flow Builder Implementation

internal class FlowBuilder<TState> : IFlowBuilder<TState> where TState : class, IFlowState
{
    public string? FlowName { get; private set; }
    public List<FlowStep> Steps { get; } = [];
    public TimeSpan DefaultTimeout { get; private set; } = TimeSpan.FromMinutes(10);
    public int DefaultRetries { get; private set; }
    public Dictionary<string, TimeSpan> TaggedTimeouts { get; } = [];
    public Dictionary<string, int> TaggedRetries { get; } = [];
    public HashSet<string> TaggedPersist { get; } = [];

    public Func<TState, IEvent>? OnFlowCompletedFactory { get; private set; }
    public Func<TState, string?, IEvent>? OnFlowFailedFactory { get; private set; }
    public Func<TState, int, IEvent>? OnStepCompletedFactory { get; private set; }
    public Func<TState, int, string?, IEvent>? OnStepFailedFactory { get; private set; }

    public IFlowBuilder<TState> Name(string name)
    {
        FlowName = name;
        return this;
    }

    public ITaggedSetting Timeout(TimeSpan timeout)
    {
        return new TaggedTimeoutSetting(this, timeout, setDefault: true);
    }

    public ITaggedSetting Retry(int maxRetries)
    {
        DefaultRetries = maxRetries;
        return new TaggedRetrySetting(this, maxRetries);
    }

    public ITaggedSetting Persist()
    {
        return new TaggedPersistSetting(this);
    }

    public IFlowBuilder<TState> OnStepCompleted<TEvent>(Func<TState, int, TEvent> factory) where TEvent : IEvent
    {
        OnStepCompletedFactory = (s, step) => factory(s, step);
        return this;
    }

    public IFlowBuilder<TState> OnStepFailed<TEvent>(Func<TState, int, string?, TEvent> factory) where TEvent : IEvent
    {
        OnStepFailedFactory = (s, step, error) => factory(s, step, error);
        return this;
    }

    public IFlowBuilder<TState> OnFlowCompleted<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent
    {
        OnFlowCompletedFactory = s => factory(s);
        return this;
    }

    public IFlowBuilder<TState> OnFlowFailed<TEvent>(Func<TState, string?, TEvent> factory) where TEvent : IEvent
    {
        OnFlowFailedFactory = (s, error) => factory(s, error);
        return this;
    }

    public IStepBuilder<TState> Send<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest
    {
        var step = new FlowStep { Type = StepType.Send, RequestFactory = factory };
        Steps.Add(step);
        return new StepBuilder<TState>(step);
    }

    public IStepBuilder<TState, TResult> Send<TResult>(Func<TState, IRequest<TResult>> factory)
    {
        var step = new FlowStep { Type = StepType.Send, HasResult = true, RequestFactory = factory };
        Steps.Add(step);
        return new StepBuilder<TState, TResult>(step);
    }

    public IQueryBuilder<TState, TResult> Query<TResult>(Func<TState, IRequest<TResult>> factory)
    {
        var step = new FlowStep { Type = StepType.Query, HasResult = true, RequestFactory = factory };
        Steps.Add(step);
        return new QueryBuilder<TState, TResult>(step);
    }

    public IPublishBuilder<TState> Publish<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent
    {
        var step = new FlowStep { Type = StepType.Publish, RequestFactory = factory };
        Steps.Add(step);
        return new PublishBuilder<TState>(step);
    }

    public IWhenAllBuilder<TState> WhenAll(params Func<TState, IRequest>[] requests)
    {
        var step = new FlowStep
        {
            Type = StepType.WhenAll,
            ChildRequestCount = requests.Length,
            ChildRequestFactories = requests.Cast<Delegate>().ToList()
        };
        Steps.Add(step);
        return new WhenAllBuilder<TState>(step);
    }

    public IWhenAnyBuilder<TState> WhenAny(params Func<TState, IRequest>[] requests)
    {
        var step = new FlowStep
        {
            Type = StepType.WhenAny,
            ChildRequestCount = requests.Length,
            ChildRequestFactories = requests.Cast<Delegate>().ToList()
        };
        Steps.Add(step);
        return new WhenAnyBuilder<TState>(step);
    }

    public IWhenAnyBuilder<TState, TResult> WhenAny<TResult>(params Func<TState, IRequest<TResult>>[] requests)
    {
        var step = new FlowStep
        {
            Type = StepType.WhenAny,
            ChildRequestCount = requests.Length,
            HasResult = true,
            ChildRequestFactories = requests.Cast<Delegate>().ToList()
        };
        Steps.Add(step);
        return new WhenAnyBuilder<TState, TResult>(step);
    }

    // Tagged settings
    private class TaggedTimeoutSetting : ITaggedSetting
    {
        private readonly FlowBuilder<TState> _builder;
        private readonly TimeSpan _timeout;
        private readonly TimeSpan _previousDefault;

        public TaggedTimeoutSetting(FlowBuilder<TState> builder, TimeSpan timeout, bool setDefault)
        {
            _builder = builder;
            _timeout = timeout;
            _previousDefault = builder.DefaultTimeout;

            // Set default immediately
            if (setDefault)
                builder.DefaultTimeout = timeout;
        }

        public void ForTags(params string[] tags)
        {
            // Revert to previous default since this is a tagged timeout
            _builder.DefaultTimeout = _previousDefault;

            foreach (var tag in tags)
                _builder.TaggedTimeouts[tag] = _timeout;
        }
    }

    private class TaggedRetrySetting(FlowBuilder<TState> builder, int retries) : ITaggedSetting
    {
        public void ForTags(params string[] tags)
        {
            foreach (var tag in tags)
                builder.TaggedRetries[tag] = retries;
        }
    }

    private class TaggedPersistSetting(FlowBuilder<TState> builder) : ITaggedSetting
    {
        public void ForTags(params string[] tags)
        {
            foreach (var tag in tags)
                builder.TaggedPersist.Add(tag);
        }
    }
}

#endregion

#region Step Builders

internal class StepBuilder<TState>(FlowStep step) : IStepBuilder<TState> where TState : class, IFlowState
{
    public IStepBuilder<TState> IfFail<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest
    {
        step.HasCompensation = true;
        step.CompensationFactory = factory;
        return this;
    }

    public IStepBuilder<TState> FailIf(Func<TState, bool> condition)
    {
        step.HasFailCondition = true;
        step.FailConditionFactory = condition;
        return this;
    }

    public IStepBuilder<TState> FailIf(Func<TState, bool> condition, string errorMessage)
    {
        step.HasFailCondition = true;
        step.FailConditionFactory = condition;
        step.FailConditionMessage = errorMessage;
        return this;
    }

    public IStepBuilder<TState> OnlyWhen(Func<TState, bool> condition)
    {
        step.HasCondition = true;
        step.ConditionFactory = condition;
        return this;
    }

    public IStepBuilder<TState> Optional()
    {
        step.IsOptional = true;
        return this;
    }

    public IStepBuilder<TState> Tag(params string[] tags)
    {
        step.Tags.AddRange(tags);
        return this;
    }

    public IStepBuilder<TState> OnCompleted<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent
    {
        step.HasOnCompletedHook = true;
        step.OnCompletedFactory = factory;
        return this;
    }

    public IStepBuilder<TState> OnFailed<TEvent>(Func<TState, string?, TEvent> factory) where TEvent : IEvent
    {
        step.HasOnFailedHook = true;
        step.OnFailedFactory = factory;
        return this;
    }
}

internal class StepBuilder<TState, TResult>(FlowStep step) : IStepBuilder<TState, TResult> where TState : class, IFlowState
{
    public IStepBuilder<TState> Into(Expression<Func<TState, TResult>> property)
    {
        if (property.Body is MemberExpression member)
        {
            step.ResultPropertyName = member.Member.Name;
            // Create a setter delegate
            var param = Expression.Parameter(typeof(TState), "s");
            var value = Expression.Parameter(typeof(TResult), "v");
            var memberAccess = Expression.MakeMemberAccess(param, member.Member);
            var assign = Expression.Assign(memberAccess, value);
            step.ResultSetter = Expression.Lambda<Action<TState, TResult>>(assign, param, value).Compile();
        }
        return new StepBuilder<TState>(step);
    }

    public IStepBuilder<TState, TResult> IfFail<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest
    {
        step.HasCompensation = true;
        step.CompensationFactory = factory;
        return this;
    }

    IStepBuilder<TState> IStepBuilder<TState>.IfFail<TRequest>(Func<TState, TRequest> factory)
    {
        step.HasCompensation = true;
        step.CompensationFactory = factory;
        return new StepBuilder<TState>(step);
    }

    public IStepBuilder<TState, TResult> FailIf(Func<TResult, bool> condition)
    {
        step.HasFailCondition = true;
        step.FailConditionFactory = condition;
        return this;
    }

    public IStepBuilder<TState, TResult> FailIf(Func<TResult, bool> condition, string errorMessage)
    {
        step.HasFailCondition = true;
        step.FailConditionFactory = condition;
        step.FailConditionMessage = errorMessage;
        return this;
    }

    IStepBuilder<TState> IStepBuilder<TState>.FailIf(Func<TState, bool> condition)
    {
        step.HasFailCondition = true;
        step.FailConditionFactory = condition;
        return new StepBuilder<TState>(step);
    }

    IStepBuilder<TState> IStepBuilder<TState>.FailIf(Func<TState, bool> condition, string errorMessage)
    {
        step.HasFailCondition = true;
        step.FailConditionFactory = condition;
        step.FailConditionMessage = errorMessage;
        return new StepBuilder<TState>(step);
    }

    public IStepBuilder<TState> OnlyWhen(Func<TState, bool> condition)
    {
        step.HasCondition = true;
        step.ConditionFactory = condition;
        return new StepBuilder<TState>(step);
    }

    public IStepBuilder<TState> Optional()
    {
        step.IsOptional = true;
        return new StepBuilder<TState>(step);
    }

    public IStepBuilder<TState> Tag(params string[] tags)
    {
        step.Tags.AddRange(tags);
        return new StepBuilder<TState>(step);
    }

    public IStepBuilder<TState> OnCompleted<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent
    {
        step.HasOnCompletedHook = true;
        step.OnCompletedFactory = factory;
        return new StepBuilder<TState>(step);
    }

    public IStepBuilder<TState> OnFailed<TEvent>(Func<TState, string?, TEvent> factory) where TEvent : IEvent
    {
        step.HasOnFailedHook = true;
        step.OnFailedFactory = factory;
        return new StepBuilder<TState>(step);
    }
}

internal class QueryBuilder<TState, TResult>(FlowStep step) : IQueryBuilder<TState, TResult> where TState : class, IFlowState
{
    public IStepBuilder<TState> Into(Expression<Func<TState, TResult>> property)
    {
        if (property.Body is MemberExpression member)
        {
            step.ResultPropertyName = member.Member.Name;
            // Create a setter delegate
            var param = Expression.Parameter(typeof(TState), "s");
            var value = Expression.Parameter(typeof(TResult), "v");
            var memberAccess = Expression.MakeMemberAccess(param, member.Member);
            var assign = Expression.Assign(memberAccess, value);
            step.ResultSetter = Expression.Lambda<Action<TState, TResult>>(assign, param, value).Compile();
        }
        return new StepBuilder<TState>(step);
    }

    public IQueryBuilder<TState, TResult> Tag(params string[] tags)
    {
        step.Tags.AddRange(tags);
        return this;
    }
}

internal class PublishBuilder<TState>(FlowStep step) : IPublishBuilder<TState> where TState : class, IFlowState
{
    public IPublishBuilder<TState> Tag(params string[] tags)
    {
        step.Tags.AddRange(tags);
        return this;
    }
}

internal class WhenAllBuilder<TState>(FlowStep step) : IWhenAllBuilder<TState> where TState : class, IFlowState
{
    public IWhenAllBuilder<TState> Timeout(TimeSpan timeout)
    {
        step.Timeout = timeout;
        return this;
    }

    public IWhenAllBuilder<TState> IfAnyFail<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest
    {
        step.HasCompensation = true;
        step.CompensationFactory = factory;
        return this;
    }

    public IWhenAllBuilder<TState> Tag(params string[] tags)
    {
        step.Tags.AddRange(tags);
        return this;
    }
}

internal class WhenAnyBuilder<TState>(FlowStep step) : IWhenAnyBuilder<TState> where TState : class, IFlowState
{
    public IWhenAnyBuilder<TState> Timeout(TimeSpan timeout)
    {
        step.Timeout = timeout;
        return this;
    }

    public IWhenAnyBuilder<TState> Tag(params string[] tags)
    {
        step.Tags.AddRange(tags);
        return this;
    }
}

internal class WhenAnyBuilder<TState, TResult>(FlowStep step) : IWhenAnyBuilder<TState, TResult> where TState : class, IFlowState
{
    public IStepBuilder<TState> Into(Expression<Func<TState, TResult>> property)
    {
        if (property.Body is MemberExpression member)
        {
            step.ResultPropertyName = member.Member.Name;
            // Create a setter delegate
            var param = Expression.Parameter(typeof(TState), "s");
            var value = Expression.Parameter(typeof(TResult), "v");
            var memberAccess = Expression.MakeMemberAccess(param, member.Member);
            var assign = Expression.Assign(memberAccess, value);
            step.ResultSetter = Expression.Lambda<Action<TState, TResult>>(assign, param, value).Compile();
        }
        return new StepBuilder<TState>(step);
    }

    public IWhenAnyBuilder<TState, TResult> Timeout(TimeSpan timeout)
    {
        step.Timeout = timeout;
        return this;
    }

    public IWhenAnyBuilder<TState, TResult> Tag(params string[] tags)
    {
        step.Tags.AddRange(tags);
        return this;
    }
}

#endregion

#region Flow Step

/// <summary>
/// Flow step metadata.
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

    // Execution delegates (set by builders)
    internal Delegate? RequestFactory { get; set; }
    internal Delegate? CompensationFactory { get; set; }
    internal Delegate? ConditionFactory { get; set; }
    internal Delegate? FailConditionFactory { get; set; }
    internal string? FailConditionMessage { get; set; }
    internal Delegate? ResultSetter { get; set; }
    internal Delegate? OnCompletedFactory { get; set; }
    internal Delegate? OnFailedFactory { get; set; }

    // WhenAll/WhenAny specific
    internal List<Delegate>? ChildRequestFactories { get; set; }
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
    WhenAny
}

#endregion
