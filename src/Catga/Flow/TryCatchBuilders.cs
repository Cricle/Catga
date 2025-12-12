using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Try-Catch builder interface.
/// </summary>
public interface ITryBuilder<TState> where TState : class, IFlowState
{
    /// <summary>
    /// Add a send step to the try block.
    /// </summary>
    ITryBuilder<TState> Send<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest;

    /// <summary>
    /// Add a query step to the try block.
    /// </summary>
    ITryBuilder<TState> Query<TResult>(Func<TState, IRequest<TResult>> factory);

    /// <summary>
    /// Add a publish step to the try block.
    /// </summary>
    ITryBuilder<TState> Publish<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent;

    /// <summary>
    /// Add a catch handler for a specific exception type.
    /// </summary>
    ITryBuilder<TState> Catch<TException>(Action<TState, TException> handler) where TException : Exception;

    /// <summary>
    /// Add a finally handler.
    /// </summary>
    ITryBuilder<TState> Finally(Action<TState> handler);

    /// <summary>
    /// End the try-catch block.
    /// </summary>
    IFlowBuilder<TState> EndTry();
}

/// <summary>
/// Implementation of try-catch builder.
/// </summary>
internal class TryBuilder<TState> : ITryBuilder<TState> where TState : class, IFlowState
{
    private readonly FlowBuilder<TState> _flowBuilder;
    private readonly FlowStep _tryStep;

    public TryBuilder(FlowBuilder<TState> flowBuilder)
    {
        _flowBuilder = flowBuilder;
        _tryStep = new FlowStep
        {
            Type = StepType.Try,
            TrySteps = [],
            CatchHandlers = []
        };
        _flowBuilder.Steps.Add(_tryStep);
    }

    public ITryBuilder<TState> Send<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest
    {
        if (_tryStep.TrySteps != null)
        {
            var step = new FlowStep { Type = StepType.Send, RequestFactory = factory };
            _tryStep.TrySteps.Add(step);
        }
        return this;
    }

    public ITryBuilder<TState> Query<TResult>(Func<TState, IRequest<TResult>> factory)
    {
        if (_tryStep.TrySteps != null)
        {
            var step = new FlowStep { Type = StepType.Query, RequestFactory = factory, HasResult = true };
            _tryStep.TrySteps.Add(step);
        }
        return this;
    }

    public ITryBuilder<TState> Publish<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent
    {
        if (_tryStep.TrySteps != null)
        {
            var step = new FlowStep { Type = StepType.Publish, RequestFactory = factory };
            _tryStep.TrySteps.Add(step);
        }
        return this;
    }

    public ITryBuilder<TState> Catch<TException>(Action<TState, TException> handler) where TException : Exception
    {
        if (_tryStep.CatchHandlers != null)
        {
            // Convert Action<TState, TException> to Action<TState, Exception>
            Action<TState, Exception> wrappedHandler = (state, ex) =>
            {
                if (ex is TException typedException)
                {
                    handler(state, typedException);
                }
            };
            _tryStep.CatchHandlers.Add((typeof(TException), (Delegate)wrappedHandler));
        }
        return this;
    }

    public ITryBuilder<TState> Finally(Action<TState> handler)
    {
        _tryStep.FinallyHandler = handler;
        return this;
    }

    public IFlowBuilder<TState> EndTry()
    {
        return _flowBuilder;
    }
}
