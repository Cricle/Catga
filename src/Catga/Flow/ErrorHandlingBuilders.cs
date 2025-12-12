using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Try block builder interface.
/// </summary>
public interface ITryBuilder<TState> where TState : class, IFlowState
{
    /// <summary>
    /// Add a send step to the try block.
    /// </summary>
    ITryBuilder<TState> Send<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest;

    /// <summary>
    /// Add a send step with result to the try block.
    /// </summary>
    ITryBuilder<TState, TResult> Send<TResult>(Func<TState, IRequest<TResult>> factory);

    /// <summary>
    /// Add a query step to the try block.
    /// </summary>
    ITryBuilder<TState, TResult> Query<TResult>(Func<TState, IRequest<TResult>> factory);

    /// <summary>
    /// Add a publish step to the try block.
    /// </summary>
    ITryBuilder<TState> Publish<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent;

    /// <summary>
    /// Add a catch block for a specific exception type.
    /// </summary>
    ICatchBuilder<TState> Catch<TException>(Action<TState, TException> handler) where TException : Exception;

    /// <summary>
    /// Add a catch block for a specific exception type with recovery.
    /// </summary>
    ICatchBuilder<TState> Catch<TException>(Func<TState, TException, IRequest> recovery) where TException : Exception;

    /// <summary>
    /// Add a finally block.
    /// </summary>
    ITryBuilder<TState> Finally(Action<TState> handler);

    /// <summary>
    /// End the try-catch block.
    /// </summary>
    IFlowBuilder<TState> EndTry();
}

/// <summary>
/// Try block builder with result.
/// </summary>
public interface ITryBuilder<TState, TResult> where TState : class, IFlowState
{
    /// <summary>
    /// Set the result into a property.
    /// </summary>
    ITryBuilder<TState> Into(System.Linq.Expressions.Expression<Func<TState, TResult>> property);
}

/// <summary>
/// Catch block builder interface.
/// </summary>
public interface ICatchBuilder<TState> where TState : class, IFlowState
{
    /// <summary>
    /// Add another catch block for a different exception type.
    /// </summary>
    ICatchBuilder<TState> Catch<TException>(Action<TState, TException> handler) where TException : Exception;

    /// <summary>
    /// Add another catch block with recovery.
    /// </summary>
    ICatchBuilder<TState> Catch<TException>(Func<TState, TException, IRequest> recovery) where TException : Exception;

    /// <summary>
    /// Add a finally block.
    /// </summary>
    ICatchBuilder<TState> Finally(Action<TState> handler);

    /// <summary>
    /// End the try-catch block.
    /// </summary>
    IFlowBuilder<TState> EndTry();
}

/// <summary>
/// Exception handler information.
/// </summary>
internal class ExceptionHandler
{
    public Type ExceptionType { get; set; } = typeof(Exception);
    public Delegate? Handler { get; set; }
    public Delegate? Recovery { get; set; }
}

/// <summary>
/// Try-catch step information.
/// </summary>
internal class TryCatchStep<TState> where TState : class, IFlowState
{
    public List<FlowStep> TrySteps { get; set; } = new();
    public List<ExceptionHandler> CatchHandlers { get; set; } = new();
    public Action<TState>? FinallyHandler { get; set; }
}

/// <summary>
/// Implementation of try block builder.
/// </summary>
internal class TryBuilder<TState> : ITryBuilder<TState> where TState : class, IFlowState
{
    private readonly FlowBuilder<TState> _flowBuilder;
    private readonly TryCatchStep<TState> _tryCatchStep;

    public TryBuilder(FlowBuilder<TState> flowBuilder)
    {
        _flowBuilder = flowBuilder;
        _tryCatchStep = new TryCatchStep<TState>();
    }

    public ITryBuilder<TState> Send<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest
    {
        // Add send step to try block
        return this;
    }

    public ITryBuilder<TState, TResult> Send<TResult>(Func<TState, IRequest<TResult>> factory)
    {
        // Add send step with result to try block
        return new TryBuilderWithResult<TState, TResult>(this);
    }

    public ITryBuilder<TState, TResult> Query<TResult>(Func<TState, IRequest<TResult>> factory)
    {
        // Add query step to try block
        return new TryBuilderWithResult<TState, TResult>(this);
    }

    public ITryBuilder<TState> Publish<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent
    {
        // Add publish step to try block
        return this;
    }

    public ICatchBuilder<TState> Catch<TException>(Action<TState, TException> handler) where TException : Exception
    {
        _tryCatchStep.CatchHandlers.Add(new ExceptionHandler
        {
            ExceptionType = typeof(TException),
            Handler = handler
        });
        return new CatchBuilder<TState>(this);
    }

    public ICatchBuilder<TState> Catch<TException>(Func<TState, TException, IRequest> recovery) where TException : Exception
    {
        _tryCatchStep.CatchHandlers.Add(new ExceptionHandler
        {
            ExceptionType = typeof(TException),
            Recovery = recovery
        });
        return new CatchBuilder<TState>(this);
    }

    public ITryBuilder<TState> Finally(Action<TState> handler)
    {
        _tryCatchStep.FinallyHandler = handler;
        return this;
    }

    public IFlowBuilder<TState> EndTry()
    {
        // Apply try-catch step to flow builder
        return _flowBuilder;
    }

    internal TryCatchStep<TState> GetTryCatchStep() => _tryCatchStep;
}

/// <summary>
/// Implementation of try block builder with result.
/// </summary>
internal class TryBuilderWithResult<TState, TResult> : ITryBuilder<TState, TResult> where TState : class, IFlowState
{
    private readonly TryBuilder<TState> _tryBuilder;

    public TryBuilderWithResult(TryBuilder<TState> tryBuilder)
    {
        _tryBuilder = tryBuilder;
    }

    public ITryBuilder<TState> Into(System.Linq.Expressions.Expression<Func<TState, TResult>> property)
    {
        // Set result into property
        return _tryBuilder;
    }
}

/// <summary>
/// Implementation of catch block builder.
/// </summary>
internal class CatchBuilder<TState> : ICatchBuilder<TState> where TState : class, IFlowState
{
    private readonly TryBuilder<TState> _tryBuilder;

    public CatchBuilder(TryBuilder<TState> tryBuilder)
    {
        _tryBuilder = tryBuilder;
    }

    public ICatchBuilder<TState> Catch<TException>(Action<TState, TException> handler) where TException : Exception
    {
        _tryBuilder.GetTryCatchStep().CatchHandlers.Add(new ExceptionHandler
        {
            ExceptionType = typeof(TException),
            Handler = handler
        });
        return this;
    }

    public ICatchBuilder<TState> Catch<TException>(Func<TState, TException, IRequest> recovery) where TException : Exception
    {
        _tryBuilder.GetTryCatchStep().CatchHandlers.Add(new ExceptionHandler
        {
            ExceptionType = typeof(TException),
            Recovery = recovery
        });
        return this;
    }

    public ICatchBuilder<TState> Finally(Action<TState> handler)
    {
        _tryBuilder.GetTryCatchStep().FinallyHandler = handler;
        return this;
    }

    public IFlowBuilder<TState> EndTry()
    {
        return _tryBuilder.EndTry();
    }
}
