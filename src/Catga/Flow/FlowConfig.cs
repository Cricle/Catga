using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
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

#region Flow Builder Implementation

// ========== FlowBuilder - Main DSL Builder ==========

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
        return new StepBuilder<TState>(this, step);
    }

    public IStepBuilder<TState, TResult> Send<TResult>(Func<TState, IRequest<TResult>> factory)
    {
        var step = new FlowStep { Type = StepType.Send, HasResult = true, RequestFactory = factory };
        Steps.Add(step);
        return new StepBuilder<TState, TResult>(this, step);
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

    public IIfBuilder<TState> If(Func<TState, bool> condition)
    {
        var step = new FlowStep
        {
            Type = StepType.If,
            BranchCondition = condition,
            ThenBranch = []
        };
        Steps.Add(step);
        return new IfBuilder<TState>(this, step, step.ThenBranch);
    }

    public ISwitchBuilder<TState, TValue> Switch<TValue>(Func<TState, TValue> selector) where TValue : notnull
    {
        var step = new FlowStep
        {
            Type = StepType.Switch,
            SwitchSelector = selector,
            Cases = []
        };
        Steps.Add(step);
        return new SwitchBuilder<TState, TValue>(this, step);
    }

    public IForEachBuilder<TState, TItem> ForEach<TItem>(Func<TState, IEnumerable<TItem>> collectionSelector)
    {
        var step = new FlowStep
        {
            Type = StepType.ForEach,
            CollectionSelector = collectionSelector,
            ItemSteps = []
        };
        Steps.Add(step);
        return new ForEachBuilder<TState, TItem>(this, step);
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

internal class StepBuilder<TState> : IStepBuilder<TState> where TState : class, IFlowState
{
    private readonly FlowBuilder<TState>? _builder;
    private readonly FlowStep _step;

    public StepBuilder(FlowStep step) : this(null, step) { }

    public StepBuilder(FlowBuilder<TState>? builder, FlowStep step)
    {
        _builder = builder;
        _step = step;
    }

    public IStepBuilder<TState> IfFail<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest
    {
        _step.HasCompensation = true;
        _step.CompensationFactory = factory;
        return this;
    }

    public IStepBuilder<TState> FailIf(Func<TState, bool> condition)
    {
        _step.HasFailCondition = true;
        _step.FailConditionFactory = condition;
        return this;
    }

    public IStepBuilder<TState> FailIf(Func<TState, bool> condition, string errorMessage)
    {
        _step.HasFailCondition = true;
        _step.FailConditionFactory = condition;
        _step.FailConditionMessage = errorMessage;
        return this;
    }

    public IStepBuilder<TState> OnlyWhen(Func<TState, bool> condition)
    {
        _step.HasCondition = true;
        _step.ConditionFactory = condition;
        return this;
    }

    public IStepBuilder<TState> Optional()
    {
        _step.IsOptional = true;
        return this;
    }

    public IStepBuilder<TState> Tag(params string[] tags)
    {
        _step.Tags.AddRange(tags);
        return this;
    }

    public IStepBuilder<TState> OnCompleted<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent
    {
        _step.HasOnCompletedHook = true;
        _step.OnCompletedFactory = factory;
        return this;
    }

    public IStepBuilder<TState> OnFailed<TEvent>(Func<TState, string?, TEvent> factory) where TEvent : IEvent
    {
        _step.HasOnFailedHook = true;
        _step.OnFailedFactory = factory;
        return this;
    }

    public IIfBuilder<TState> If(Func<TState, bool> condition)
    {
        if (_builder == null)
            throw new InvalidOperationException("Cannot use If() without a FlowBuilder context");
        return _builder.If(condition);
    }

    public ISwitchBuilder<TState, TValue> Switch<TValue>(Func<TState, TValue> selector) where TValue : notnull
    {
        if (_builder == null)
            throw new InvalidOperationException("Cannot use Switch() without a FlowBuilder context");
        return _builder.Switch(selector);
    }
}

internal class StepBuilder<TState, TResult> : IStepBuilder<TState, TResult> where TState : class, IFlowState
{
    private readonly FlowBuilder<TState> _builder;
    private readonly FlowStep _step;

    public StepBuilder(FlowBuilder<TState> builder, FlowStep step)
    {
        _builder = builder;
        _step = step;
    }

    [RequiresDynamicCode("Into uses expression compilation")]
    public IStepBuilder<TState> Into(Expression<Func<TState, TResult>> property)
    {
        try
        {
            // Create a setter delegate for any expression type
            var param = Expression.Parameter(typeof(TState), "s");
            var value = Expression.Parameter(typeof(TResult), "v");

            // Replace the parameter in the property expression with our parameter
            var visitor = new ParameterReplacer(property.Parameters[0], param);
            var targetExpression = visitor.Visit(property.Body);

            Expression assign;

            // Handle indexer expressions specially
            if (targetExpression is MethodCallExpression methodCall &&
                methodCall.Method.Name == "get_Item" &&
                methodCall.Object != null)
            {
                // This is an indexer access like dict[key]
                // We need to call the set_Item method instead
                var setMethod = methodCall.Object.Type.GetMethod("set_Item");
                if (setMethod != null)
                {
                    // Create a call to set_Item(key, value)
                    assign = Expression.Call(methodCall.Object, setMethod, methodCall.Arguments[0], value);
                }
                else
                {
                    throw new InvalidOperationException("Cannot find setter for indexer");
                }
            }
            else
            {
                // Regular assignment for properties
                assign = Expression.Assign(targetExpression, value);
            }

            _step.ResultSetter = Expression.Lambda<Action<TState, TResult>>(assign, param, value).Compile();

            // Set property name for simple member access
            if (property.Body is MemberExpression member)
            {
                _step.ResultPropertyName = member.Member.Name;
            }

            // Successfully compiled Into expression
        }
        catch (Exception)
        {
            // Failed to compile Into expression - fall back to null setter
            // In production, this should use proper logging
            _step.ResultSetter = null;
        }

        return new StepBuilder<TState>(_builder, _step);
    }

    /// <summary>
    /// Into overload that accepts a lambda with both state and result parameters.
    /// Useful for complex assignments like s => s.ExecutedCases.Add(result).
    /// </summary>
    [RequiresDynamicCode("Into uses expression compilation")]
    public IStepBuilder<TState> Into(Action<TState, TResult> setter)
    {
        _step.ResultSetter = setter;
        return new StepBuilder<TState>(_builder, _step);
    }

    public IStepBuilder<TState, TResult> IfFail<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest
    {
        _step.HasCompensation = true;
        _step.CompensationFactory = factory;
        return this;
    }

    IStepBuilder<TState> IStepBuilder<TState>.IfFail<TRequest>(Func<TState, TRequest> factory)
    {
        _step.HasCompensation = true;
        _step.CompensationFactory = factory;
        return new StepBuilder<TState>(_step);
    }

    public IStepBuilder<TState, TResult> FailIf(Func<TResult, bool> condition)
    {
        _step.HasFailCondition = true;
        _step.FailConditionFactory = condition;
        return this;
    }

    public IStepBuilder<TState, TResult> FailIf(Func<TResult, bool> condition, string errorMessage)
    {
        _step.HasFailCondition = true;
        _step.FailConditionFactory = condition;
        _step.FailConditionMessage = errorMessage;
        return this;
    }

    IStepBuilder<TState> IStepBuilder<TState>.FailIf(Func<TState, bool> condition)
    {
        _step.HasFailCondition = true;
        _step.FailConditionFactory = condition;
        return new StepBuilder<TState>(_step);
    }

    IStepBuilder<TState> IStepBuilder<TState>.FailIf(Func<TState, bool> condition, string errorMessage)
    {
        _step.HasFailCondition = true;
        _step.FailConditionFactory = condition;
        _step.FailConditionMessage = errorMessage;
        return new StepBuilder<TState>(_step);
    }

    public IStepBuilder<TState> OnlyWhen(Func<TState, bool> condition)
    {
        _step.HasCondition = true;
        _step.ConditionFactory = condition;
        return new StepBuilder<TState>(_step);
    }

    public IStepBuilder<TState> Optional()
    {
        _step.IsOptional = true;
        return new StepBuilder<TState>(_step);
    }

    public IStepBuilder<TState> Tag(params string[] tags)
    {
        _step.Tags.AddRange(tags);
        return new StepBuilder<TState>(_step);
    }

    public IStepBuilder<TState> OnCompleted<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent
    {
        _step.HasOnCompletedHook = true;
        _step.OnCompletedFactory = factory;
        return new StepBuilder<TState>(_step);
    }

    public IStepBuilder<TState> OnFailed<TEvent>(Func<TState, string?, TEvent> factory) where TEvent : IEvent
    {
        _step.HasOnFailedHook = true;
        _step.OnFailedFactory = factory;
        return new StepBuilder<TState>(_builder, _step);
    }

    public IIfBuilder<TState> If(Func<TState, bool> condition) => _builder.If(condition);

    public ISwitchBuilder<TState, TValue> Switch<TValue>(Func<TState, TValue> selector) where TValue : notnull
        => _builder.Switch(selector);
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

#region If/Switch Builders

internal class IfBuilder<TState> : IIfBuilder<TState> where TState : class, IFlowState
{
    private readonly FlowBuilder<TState> _rootBuilder;
    private FlowStep _ifStep;
    private List<FlowStep> _currentBranch;
    private readonly Stack<(FlowStep Step, List<FlowStep> Branch)> _nestedStack = new();

    public IfBuilder(FlowBuilder<TState> rootBuilder, FlowStep ifStep, List<FlowStep> currentBranch)
    {
        _rootBuilder = rootBuilder;
        _ifStep = ifStep;
        _currentBranch = currentBranch;
    }

    public IIfBuilder<TState> Send<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest
    {
        var step = new FlowStep { Type = StepType.Send, RequestFactory = factory };
        _currentBranch.Add(step);
        return this;
    }

    public IIfBuilder<TState, TResult> Send<TResult>(Func<TState, IRequest<TResult>> factory)
    {
        var step = new FlowStep { Type = StepType.Send, HasResult = true, RequestFactory = factory };
        _currentBranch.Add(step);
        return new IfBuilderWithResult<TState, TResult>(this, step);
    }

    public IIfBuilder<TState> Publish<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent
    {
        var step = new FlowStep { Type = StepType.Publish, RequestFactory = factory };
        _currentBranch.Add(step);
        return this;
    }

    public IIfBuilder<TState> If(Func<TState, bool> condition)
    {
        // Nested If - add to current branch and switch context
        var nestedStep = new FlowStep
        {
            Type = StepType.If,
            BranchCondition = condition,
            ThenBranch = []
        };
        _currentBranch.Add(nestedStep);
        // Save current context and switch to nested If's Then branch
        _nestedStack.Push((_ifStep, _currentBranch));
        _ifStep = nestedStep;
        _currentBranch = nestedStep.ThenBranch;
        return this;
    }

    public IIfBuilder<TState> ElseIf(Func<TState, bool> condition)
    {
        _ifStep.ElseIfBranches ??= [];
        var branch = new List<FlowStep>();
        _ifStep.ElseIfBranches.Add((condition, branch));
        _currentBranch = branch;
        return this;
    }

    public IIfBuilder<TState> Else()
    {
        _ifStep.ElseBranch = [];
        _currentBranch = _ifStep.ElseBranch;
        return this;
    }

    public IForEachBuilder<TState, TItem> ForEach<TItem>(Func<TState, IEnumerable<TItem>> collectionSelector)
    {
        var step = new FlowStep
        {
            Type = StepType.ForEach,
            CollectionSelector = collectionSelector,
            ItemSteps = []
        };
        _currentBranch.Add(step);
        return new ForEachBuilder<TState, TItem>(_rootBuilder, step);
    }

    public IIfBuilder<TState> EndIf()
    {
        // Pop from nested stack to restore parent context
        if (_nestedStack.Count > 0)
        {
            var (parentStep, parentBranch) = _nestedStack.Pop();
            _ifStep = parentStep;
            _currentBranch = parentBranch;
        }
        return this;
    }
}

internal class IfBuilderWithResult<TState, TResult> : IIfBuilder<TState, TResult> where TState : class, IFlowState
{
    private readonly IfBuilder<TState> _parent;
    private readonly FlowStep _step;

    public IfBuilderWithResult(IfBuilder<TState> parent, FlowStep step)
    {
        _parent = parent;
        _step = step;
    }

    public IIfBuilder<TState> Into(Expression<Func<TState, TResult>> property)
    {
        if (property.Body is MemberExpression member)
        {
            _step.ResultPropertyName = member.Member.Name;
            var param = Expression.Parameter(typeof(TState), "s");
            var value = Expression.Parameter(typeof(TResult), "v");
            var memberAccess = Expression.MakeMemberAccess(param, member.Member);
            var assign = Expression.Assign(memberAccess, value);
            _step.ResultSetter = Expression.Lambda<Action<TState, TResult>>(assign, param, value).Compile();
        }
        return _parent;
    }

    public IIfBuilder<TState> Into(Action<TState, TResult> setter)
    {
        _step.ResultSetter = setter;
        return _parent;
    }
}

internal class SwitchBuilder<TState, TValue> : ISwitchBuilder<TState, TValue>
    where TState : class, IFlowState
    where TValue : notnull
{
    private readonly FlowBuilder<TState> _rootBuilder;
    private readonly FlowStep _switchStep;

    public SwitchBuilder(FlowBuilder<TState> rootBuilder, FlowStep switchStep)
    {
        _rootBuilder = rootBuilder;
        _switchStep = switchStep;
    }

    public ISwitchBuilder<TState, TValue> Case(TValue value, Action<ICaseBuilder<TState>> configure)
    {
        var steps = new List<FlowStep>();
        var caseBuilder = new CaseBuilder<TState>(steps);
        configure(caseBuilder);
        _switchStep.Cases![value] = steps;
        return this;
    }

    public ISwitchBuilder<TState, TValue> Default(Action<ICaseBuilder<TState>> configure)
    {
        var steps = new List<FlowStep>();
        var caseBuilder = new CaseBuilder<TState>(steps);
        configure(caseBuilder);
        _switchStep.DefaultBranch = steps;
        return this;
    }

    public IFlowBuilder<TState> EndSwitch() => _rootBuilder;
}

internal class CaseBuilder<TState> : ICaseBuilder<TState> where TState : class, IFlowState
{
    private readonly List<FlowStep> _steps;

    public CaseBuilder(List<FlowStep> steps) => _steps = steps;

    public ICaseBuilder<TState> Send<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest
    {
        var step = new FlowStep { Type = StepType.Send, RequestFactory = factory };
        _steps.Add(step);
        return this;
    }

    public ICaseBuilder<TState, TResult> Send<TResult>(Func<TState, IRequest<TResult>> factory)
    {
        var step = new FlowStep { Type = StepType.Send, HasResult = true, RequestFactory = factory };
        _steps.Add(step);
        return new CaseBuilderWithResult<TState, TResult>(this, step);
    }

    public ICaseBuilder<TState> Publish<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent
    {
        var step = new FlowStep { Type = StepType.Publish, RequestFactory = factory };
        _steps.Add(step);
        return this;
    }
}

internal class CaseBuilderWithResult<TState, TResult> : ICaseBuilder<TState, TResult> where TState : class, IFlowState
{
    private readonly CaseBuilder<TState> _parent;
    private readonly FlowStep _step;

    public CaseBuilderWithResult(CaseBuilder<TState> parent, FlowStep step)
    {
        _parent = parent;
        _step = step;
    }

    public ICaseBuilder<TState> Into(Expression<Func<TState, TResult>> property)
    {
        if (property.Body is MemberExpression member)
        {
            _step.ResultPropertyName = member.Member.Name;
            var param = Expression.Parameter(typeof(TState), "s");
            var value = Expression.Parameter(typeof(TResult), "v");
            var memberAccess = Expression.MakeMemberAccess(param, member.Member);
            var assign = Expression.Assign(memberAccess, value);
            _step.ResultSetter = Expression.Lambda<Action<TState, TResult>>(assign, param, value).Compile();
        }
        return _parent;
    }

    public ICaseBuilder<TState> Into(Action<TState, TResult> setter)
    {
        _step.ResultSetter = setter;
        return _parent;
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

    // Branching (If/Switch)
    /// <summary>Condition for If step.</summary>
    internal Delegate? BranchCondition { get; set; }
    /// <summary>Then branch steps.</summary>
    public List<FlowStep>? ThenBranch { get; set; }
    /// <summary>Else branch steps.</summary>
    public List<FlowStep>? ElseBranch { get; set; }
    /// <summary>ElseIf conditions and branches.</summary>
    public List<(Delegate Condition, List<FlowStep> Steps)>? ElseIfBranches { get; set; }

    // Switch specific
    /// <summary>Switch selector.</summary>
    internal Delegate? SwitchSelector { get; set; }
    /// <summary>Case branches: value -> steps.</summary>
    public Dictionary<object, List<FlowStep>>? Cases { get; set; }
    /// <summary>Default branch for Switch.</summary>
    public List<FlowStep>? DefaultBranch { get; set; }

    // ForEach specific
    /// <summary>Collection selector for ForEach.</summary>
    internal Delegate? CollectionSelector { get; set; }
    /// <summary>Steps to execute for each item.</summary>
    public List<FlowStep>? ItemSteps { get; set; }
    /// <summary>Configurator delegate for building item steps at runtime.</summary>
    internal Delegate? ItemStepsConfigurator { get; set; }
    /// <summary>Batch size for processing items.</summary>
    public int BatchSize { get; set; } = 100;
    /// <summary>Maximum degree of parallelism for processing items.</summary>
    internal int? MaxDegreeOfParallelism { get; set; }
    internal bool StreamingEnabled { get; set; } = false;
    internal bool MetricsEnabled { get; set; } = false;
    internal bool CircuitBreakerEnabled { get; set; } = false;
    internal int CircuitBreakerFailureThreshold { get; set; } = 5;
    internal TimeSpan CircuitBreakerBreakDuration { get; set; } = TimeSpan.FromMinutes(1);
    /// <summary>Failure handling strategy.</summary>
    public ForEachFailureHandling FailureHandling { get; set; } = ForEachFailureHandling.StopOnFirstFailure;
    /// <summary>Item success callback.</summary>
    internal Delegate? OnItemSuccess { get; set; }
    /// <summary>Item failure callback.</summary>
    internal Delegate? OnItemFail { get; set; }
    /// <summary>Completion callback.</summary>
    internal Delegate? OnComplete { get; set; }
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

/// <summary>
/// Expression visitor to replace parameters in expressions.
/// </summary>
internal class ParameterReplacer : ExpressionVisitor
{
    private readonly ParameterExpression _oldParameter;
    private readonly ParameterExpression _newParameter;

    public ParameterReplacer(ParameterExpression oldParameter, ParameterExpression newParameter)
    {
        _oldParameter = oldParameter;
        _newParameter = newParameter;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        return node == _oldParameter ? _newParameter : base.VisitParameter(node);
    }
}

#endregion
