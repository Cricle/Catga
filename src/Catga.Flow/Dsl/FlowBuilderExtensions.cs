using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Flow.Dsl;

/// <summary>
/// Extension methods for IFlowBuilder to add steps (Send, Query, Publish, etc).
/// These are extension methods to allow easy extensibility without modifying core interfaces.
/// </summary>
public static class FlowBuilderExtensions
{
    /// <summary>Add a Send step that sends a request without expecting a result.</summary>
    public static IStepBuilder<TState> Send<TState, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(
        this IFlowBuilder<TState> builder,
        Func<TState, TRequest> factory)
        where TState : class, IFlowState
        where TRequest : IRequest
    {
        var flowBuilder = GetFlowBuilder(builder);
        var step = new FlowStep
        {
            Type = StepType.Send,
            RequestFactory = factory,
            CreateRequest = state => factory((TState)state),
            ExecuteRequest = async (mediator, request, ct) =>
            {
                var result = await mediator.SendAsync((TRequest)request, ct);
                return (result.IsSuccess, result.Error, null);
            }
        };
        flowBuilder.Steps.Add(step);
        return new StepBuilder<TState>(flowBuilder, step);
    }

    /// <summary>Add a Send step that sends a request and expects a result.</summary>
    public static IStepBuilder<TState, TResult> Send<TState, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(
        this IFlowBuilder<TState> builder,
        Func<TState, TRequest> factory)
        where TState : class, IFlowState
        where TRequest : IRequest<TResult>
    {
        var flowBuilder = GetFlowBuilder(builder);
        var step = CreateRequestStep<TState, TRequest, TResult>(StepType.Send, factory);
        flowBuilder.Steps.Add(step);
        return new StepBuilder<TState, TResult>(flowBuilder, step);
    }

    /// <summary>Add a Query step that sends a query and expects a result.</summary>
    public static IQueryBuilder<TState, TResult> Query<TState, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(
        this IFlowBuilder<TState> builder,
        Func<TState, TRequest> factory)
        where TState : class, IFlowState
        where TRequest : IRequest<TResult>
    {
        var flowBuilder = GetFlowBuilder(builder);
        var step = CreateRequestStep<TState, TRequest, TResult>(StepType.Query, factory);
        flowBuilder.Steps.Add(step);
        return new QueryBuilder<TState, TResult>(step);
    }

    /// <summary>Add a Publish step that publishes an event.</summary>
    public static IPublishBuilder<TState> Publish<TState, TEvent>(
        this IFlowBuilder<TState> builder,
        Func<TState, TEvent> factory)
        where TState : class, IFlowState
        where TEvent : IEvent
    {
        var flowBuilder = GetFlowBuilder(builder);
        var step = new FlowStep
        {
            Type = StepType.Publish,
            RequestFactory = factory,
            CreateRequest = state => factory((TState)state)!
        };
        flowBuilder.Steps.Add(step);
        return new PublishBuilder<TState>(step);
    }

    /// <summary>Add a WhenAll step that executes multiple requests in parallel.</summary>
    public static IWhenAllBuilder<TState> WhenAll<TState>(
        this IFlowBuilder<TState> builder,
        params Func<TState, IRequest>[] requests)
        where TState : class, IFlowState
    {
        var flowBuilder = GetFlowBuilder(builder);
        var step = new FlowStep
        {
            Type = StepType.WhenAll,
            ChildRequestCount = requests.Length,
            ChildRequestFactories = requests.Cast<Delegate>().ToList(),
            StartChildRequests = requests.Select<Func<TState, IRequest>, Func<ICatgaMediator, object, CancellationToken, ValueTask>>(
                factory => async (mediator, state, ct) =>
                {
                    await mediator.SendAsync(factory((TState)state), ct);
                }).ToList(),
            CreateChildRequests = requests.Select<Func<TState, IRequest>, Func<object, IRequest>>(
                f => state => f((TState)state)).ToList()
        };
        flowBuilder.Steps.Add(step);
        return new WhenAllBuilder<TState>(step);
    }

    /// <summary>Add a WhenAny step that executes multiple requests and waits for any to complete.</summary>
    public static IWhenAnyBuilder<TState> WhenAny<TState>(
        this IFlowBuilder<TState> builder,
        params Func<TState, IRequest>[] requests)
        where TState : class, IFlowState
    {
        var flowBuilder = GetFlowBuilder(builder);
        var step = new FlowStep
        {
            Type = StepType.WhenAny,
            ChildRequestCount = requests.Length,
            ChildRequestFactories = requests.Cast<Delegate>().ToList(),
            StartChildRequests = requests.Select<Func<TState, IRequest>, Func<ICatgaMediator, object, CancellationToken, ValueTask>>(
                factory => async (mediator, state, ct) =>
                {
                    await mediator.SendAsync(factory((TState)state), ct);
                }).ToList(),
            CreateChildRequests = requests.Select<Func<TState, IRequest>, Func<object, IRequest>>(
                f => state => f((TState)state)).ToList()
        };
        flowBuilder.Steps.Add(step);
        return new WhenAnyBuilder<TState>(step);
    }

    /// <summary>Add a WhenAny step with result.</summary>
    public static IWhenAnyBuilder<TState, TResult> WhenAny<TState, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(
        this IFlowBuilder<TState> builder,
        params Func<TState, IRequest<TResult>>[] requests)
        where TState : class, IFlowState
    {
        var flowBuilder = GetFlowBuilder(builder);
        var step = new FlowStep
        {
            Type = StepType.WhenAny,
            ChildRequestCount = requests.Length,
            HasResult = true,
            ChildRequestFactories = requests.Cast<Delegate>().ToList(),
            StartChildRequests = requests.Select<Func<TState, IRequest<TResult>>, Func<ICatgaMediator, object, CancellationToken, ValueTask>>(
                factory => async (mediator, state, ct) =>
                {
                    await mediator.SendAsync<IRequest<TResult>, TResult>(factory((TState)state), ct);
                }).ToList()
        };
        flowBuilder.Steps.Add(step);
        return new WhenAnyBuilder<TState, TResult>(step);
    }

    /// <summary>Add an If branching step.</summary>
    public static IIfBuilder<TState> If<TState>(
        this IFlowBuilder<TState> builder,
        Func<TState, bool> condition)
        where TState : class, IFlowState
    {
        var flowBuilder = GetFlowBuilder(builder);
        var step = new FlowStep
        {
            Type = StepType.If,
            BranchCondition = condition,
            EvaluateBranchCondition = state => condition((TState)state),
            ThenBranch = []
        };
        flowBuilder.Steps.Add(step);
        return new IfBuilder<TState>(flowBuilder, step, step.ThenBranch);
    }

    /// <summary>Add a Switch branching step.</summary>
    public static ISwitchBuilder<TState, TValue> Switch<TState, TValue>(
        this IFlowBuilder<TState> builder,
        Func<TState, TValue> selector)
        where TState : class, IFlowState
        where TValue : notnull
    {
        var flowBuilder = GetFlowBuilder(builder);
        var step = new FlowStep
        {
            Type = StepType.Switch,
            SwitchSelector = selector,
            EvaluateSwitchSelector = state => selector((TState)state)!,
            Cases = []
        };
        flowBuilder.Steps.Add(step);
        return new SwitchBuilder<TState, TValue>(flowBuilder, step);
    }

    /// <summary>Add a ForEach step that iterates over a collection.</summary>
    public static IForEachBuilder<TState, TItem> ForEach<TState, TItem>(
        this IFlowBuilder<TState> builder,
        Func<TState, IEnumerable<TItem>> collectionSelector)
        where TState : class, IFlowState
    {
        var flowBuilder = GetFlowBuilder(builder);
        var step = new FlowStep
        {
            Type = StepType.ForEach,
            CollectionSelector = collectionSelector,
            GetCollection = state => collectionSelector((TState)state).Cast<object>(),
            ItemSteps = []
        };
        flowBuilder.Steps.Add(step);
        return new ForEachBuilder<TState, TItem>(flowBuilder, step);
    }

    /// <summary>
    /// Create a request step with result (DRY helper for Send and Query).
    /// Extracted to reduce code duplication between Send and Query methods.
    /// </summary>
    private static FlowStep CreateRequestStep<TState, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(
        StepType stepType,
        Func<TState, TRequest> factory)
        where TState : class, IFlowState
        where TRequest : IRequest<TResult>
    {
        return new FlowStep
        {
            Type = stepType,
            HasResult = true,
            RequestFactory = factory,
            CreateRequest = state => factory((TState)state),
            ExecuteRequest = async (mediator, request, ct) =>
            {
                var typedRequest = (TRequest)request;
                var result = await mediator.SendAsync<TRequest, TResult>(typedRequest, ct);
                return (result.IsSuccess, result.Error, result.Value);
            }
        };
    }

    // Helper to get FlowBuilder from IFlowBuilder
    internal static FlowBuilder<TState> GetFlowBuilder<TState>(IFlowBuilder<TState> builder)
        where TState : class, IFlowState
    {
        if (builder is FlowBuilder<TState> flowBuilder)
            return flowBuilder;
        throw new InvalidOperationException("Builder must be FlowBuilder<TState>");
    }

    /// <summary>
    /// Add a Parallel step that executes multiple independent step branches concurrently.
    /// Unlike WhenAll (which runs multiple requests), Parallel runs full step sequences.
    /// </summary>
    public static IParallelBuilder<TState> Parallel<TState>(
        this IFlowBuilder<TState> builder)
        where TState : class, IFlowState
    {
        var flowBuilder = GetFlowBuilder(builder);
        var step = new FlowStep { Type = StepType.Parallel, ParallelWaitAll = true };
        flowBuilder.Steps.Add(step);
        return new ParallelBuilder<TState>(flowBuilder, step);
    }

    /// <summary>
    /// Add a Throttle step that limits concurrent execution of inner steps.
    /// </summary>
    public static IThrottleBuilder<TState> Throttle<TState>(
        this IFlowBuilder<TState> builder,
        int maxConcurrency)
        where TState : class, IFlowState
    {
        var flowBuilder = GetFlowBuilder(builder);
        var step = new FlowStep { Type = StepType.Throttle, ThrottleCount = maxConcurrency };
        flowBuilder.Steps.Add(step);
        return new ThrottleBuilder<TState>(flowBuilder, step);
    }

    /// <summary>
    /// Add per-step retry configuration. Overrides global retry for this step.
    /// </summary>
    public static IStepBuilder<TState> Retry<TState>(
        this IStepBuilder<TState> builder,
        int maxAttempts,
        TimeSpan? delay = null)
        where TState : class, IFlowState
    {
        if (builder is StepBuilder<TState> sb)
        {
            sb.Step.RetryCount = maxAttempts;
            sb.Step.RetryDelay = delay ?? TimeSpan.FromMilliseconds(100);
        }
        return builder;
    }

    /// <summary>
    /// Add per-step retry configuration for steps with result.
    /// </summary>
    public static IStepBuilder<TState, TResult> Retry<TState, TResult>(
        this IStepBuilder<TState, TResult> builder,
        int maxAttempts,
        TimeSpan? delay = null)
        where TState : class, IFlowState
    {
        if (builder is StepBuilder<TState, TResult> sb)
        {
            sb.Step.RetryCount = maxAttempts;
            sb.Step.RetryDelay = delay ?? TimeSpan.FromMilliseconds(100);
        }
        return builder;
    }

    /// <summary>
    /// Add a RemoteSend step that calls a remote service via IRequestClientFactory
    /// and awaits the response. Equivalent to MassTransit's Request/Response in a saga.
    /// Requires IRequestClientFactory to be registered in DI.
    /// </summary>
    public static IStepBuilder<TState, TResult> RemoteSend<TState,
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TRequest,
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TResult>(
        this IFlowBuilder<TState> builder,
        Func<TState, TRequest> factory,
        string? destination = null,
        TimeSpan? timeout = null)
        where TState : class, IFlowState
        where TRequest : class, Catga.Abstractions.IRequest<TResult>
        where TResult : class
    {
        var flowBuilder = GetFlowBuilder(builder);
        var step = new FlowStep
        {
            Type = StepType.RemoteSend,
            HasResult = true,
            RequestFactory = factory,
            CreateRequest = state => factory((TState)state),
            ExecuteRemoteRequest = async (requestClientFactory, request, ct) =>
            {
                var typedRequest = (TRequest)request;
                var client = requestClientFactory.CreateClient<TRequest, TResult>(destination, timeout);
                var result = await client.RequestAsync(typedRequest, ct);
                return (result.IsSuccess, result.Error, result.Value);
            }
        };
        // Store remote send metadata
        step.Tags.Add($"__remote_dest:{destination ?? typeof(TRequest).Name}");
        if (timeout.HasValue)
            step.Timeout = timeout;

        flowBuilder.Steps.Add(step);
        return new StepBuilder<TState, TResult>(flowBuilder, step);
    }
}
