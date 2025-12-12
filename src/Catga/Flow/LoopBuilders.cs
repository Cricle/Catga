using System.Linq.Expressions;
using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// While loop builder interface.
/// </summary>
public interface IWhileBuilder<TState> where TState : class, IFlowState
{
    /// <summary>
    /// Add a send step to the while loop.
    /// </summary>
    IWhileBuilder<TState> Send<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest;

    /// <summary>
    /// Add a send step with result to the while loop.
    /// </summary>
    IWhileBuilder<TState, TResult> Send<TResult>(Func<TState, IRequest<TResult>> factory);

    /// <summary>
    /// Add a query step to the while loop.
    /// </summary>
    IWhileBuilder<TState, TResult> Query<TResult>(Func<TState, IRequest<TResult>> factory);

    /// <summary>
    /// Add a publish step to the while loop.
    /// </summary>
    IWhileBuilder<TState> Publish<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent;

    /// <summary>
    /// Add a condition to break the loop.
    /// </summary>
    IWhileBuilder<TState> BreakIf(Expression<Func<TState, bool>> condition);

    /// <summary>
    /// Add a condition to continue the loop.
    /// </summary>
    IWhileBuilder<TState> ContinueIf(Expression<Func<TState, bool>> condition);

    /// <summary>
    /// End the while loop.
    /// </summary>
    IFlowBuilder<TState> EndWhile();
}

/// <summary>
/// While loop builder with result.
/// </summary>
public interface IWhileBuilder<TState, TResult> where TState : class, IFlowState
{
    /// <summary>
    /// Set the result into a property.
    /// </summary>
    IWhileBuilder<TState> Into(Expression<Func<TState, TResult>> property);
}

/// <summary>
/// Do-While loop builder interface.
/// </summary>
public interface IDoWhileBuilder<TState> where TState : class, IFlowState
{
    /// <summary>
    /// Add a send step to the do-while loop.
    /// </summary>
    IDoWhileBuilder<TState> Send<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest;

    /// <summary>
    /// Add a send step with result to the do-while loop.
    /// </summary>
    IDoWhileBuilder<TState, TResult> Send<TResult>(Func<TState, IRequest<TResult>> factory);

    /// <summary>
    /// Add a query step to the do-while loop.
    /// </summary>
    IDoWhileBuilder<TState, TResult> Query<TResult>(Func<TState, IRequest<TResult>> factory);

    /// <summary>
    /// Add a publish step to the do-while loop.
    /// </summary>
    IDoWhileBuilder<TState> Publish<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent;

    /// <summary>
    /// End the do-while loop with condition.
    /// </summary>
    IFlowBuilder<TState> Until(Expression<Func<TState, bool>> condition);
}

/// <summary>
/// Do-While loop builder with result.
/// </summary>
public interface IDoWhileBuilder<TState, TResult> where TState : class, IFlowState
{
    /// <summary>
    /// Set the result into a property.
    /// </summary>
    IDoWhileBuilder<TState> Into(Expression<Func<TState, TResult>> property);
}

/// <summary>
/// Repeat loop builder interface.
/// </summary>
public interface IRepeatBuilder<TState> where TState : class, IFlowState
{
    /// <summary>
    /// Add a send step to the repeat loop.
    /// </summary>
    IRepeatBuilder<TState> Send<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest;

    /// <summary>
    /// Add a send step with result to the repeat loop.
    /// </summary>
    IRepeatBuilder<TState, TResult> Send<TResult>(Func<TState, IRequest<TResult>> factory);

    /// <summary>
    /// Add a query step to the repeat loop.
    /// </summary>
    IRepeatBuilder<TState, TResult> Query<TResult>(Func<TState, IRequest<TResult>> factory);

    /// <summary>
    /// Add a publish step to the repeat loop.
    /// </summary>
    IRepeatBuilder<TState> Publish<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent;

    /// <summary>
    /// Add a condition to break the loop.
    /// </summary>
    IRepeatBuilder<TState> BreakIf(Expression<Func<TState, bool>> condition);

    /// <summary>
    /// End the repeat loop.
    /// </summary>
    IFlowBuilder<TState> EndRepeat();
}

/// <summary>
/// Repeat loop builder with result.
/// </summary>
public interface IRepeatBuilder<TState, TResult> where TState : class, IFlowState
{
    /// <summary>
    /// Set the result into a property.
    /// </summary>
    IRepeatBuilder<TState> Into(Expression<Func<TState, TResult>> property);
}

/// <summary>
/// Loop step type enumeration.
/// </summary>
internal enum LoopType
{
    While,
    DoWhile,
    Repeat
}

/// <summary>
/// Internal representation of a loop step.
/// </summary>
internal class LoopStep<TState> where TState : class, IFlowState
{
    public LoopType Type { get; set; }
    public Expression<Func<TState, bool>>? Condition { get; set; }
    public Expression<Func<TState, int>>? TimesSelector { get; set; }
    public int? FixedTimes { get; set; }
    public List<FlowStep> Steps { get; set; } = new();
    public Expression<Func<TState, bool>>? BreakCondition { get; set; }
    public Expression<Func<TState, bool>>? ContinueCondition { get; set; }
}

/// <summary>
/// Implementation of while loop builder.
/// </summary>
internal class WhileBuilder<TState> : IWhileBuilder<TState> where TState : class, IFlowState
{
    private readonly FlowBuilder<TState> _flowBuilder;
    private readonly LoopStep<TState> _loopStep;

    public WhileBuilder(FlowBuilder<TState> flowBuilder, Func<TState, bool> condition)
    {
        _flowBuilder = flowBuilder;
        _loopStep = new LoopStep<TState>
        {
            Type = LoopType.While,
            Condition = null // Will be set when EndWhile is called
        };

        // Create the FlowStep for the while loop
        var flowStep = new FlowStep
        {
            Type = StepType.While,
            LoopCondition = condition,
            LoopSteps = []
        };
        _flowBuilder.Steps.Add(flowStep);
    }

    public IWhileBuilder<TState> Send<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest
    {
        // Add send step to loop
        if (_flowBuilder.Steps.Count > 0)
        {
            var lastStep = _flowBuilder.Steps[^1];
            if (lastStep.Type == StepType.While && lastStep.LoopSteps != null)
            {
                var step = new FlowStep { Type = StepType.Send, RequestFactory = factory };
                lastStep.LoopSteps.Add(step);
            }
        }
        return this;
    }

    public IWhileBuilder<TState, TResult> Send<TResult>(Func<TState, IRequest<TResult>> factory)
    {
        // Add send step with result to loop
        return new WhileBuilderWithResult<TState, TResult>(this);
    }

    public IWhileBuilder<TState, TResult> Query<TResult>(Func<TState, IRequest<TResult>> factory)
    {
        // Add query step to loop
        return new WhileBuilderWithResult<TState, TResult>(this);
    }

    public IWhileBuilder<TState> Publish<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent
    {
        // Add publish step to loop
        return this;
    }

    public IWhileBuilder<TState> BreakIf(Expression<Func<TState, bool>> condition)
    {
        _loopStep.BreakCondition = condition;
        return this;
    }

    public IWhileBuilder<TState> ContinueIf(Expression<Func<TState, bool>> condition)
    {
        _loopStep.ContinueCondition = condition;
        return this;
    }

    public IFlowBuilder<TState> EndWhile()
    {
        // Apply loop step to flow builder
        return _flowBuilder;
    }
}

/// <summary>
/// Implementation of while loop builder with result.
/// </summary>
internal class WhileBuilderWithResult<TState, TResult> : IWhileBuilder<TState, TResult> where TState : class, IFlowState
{
    private readonly WhileBuilder<TState> _whileBuilder;

    public WhileBuilderWithResult(WhileBuilder<TState> whileBuilder)
    {
        _whileBuilder = whileBuilder;
    }

    public IWhileBuilder<TState> Into(Expression<Func<TState, TResult>> property)
    {
        // Set result into property
        return _whileBuilder;
    }
}

/// <summary>
/// Implementation of do-while loop builder.
/// </summary>
internal class DoWhileBuilder<TState> : IDoWhileBuilder<TState> where TState : class, IFlowState
{
    private readonly FlowBuilder<TState> _flowBuilder;
    private readonly LoopStep<TState> _loopStep;

    public DoWhileBuilder(FlowBuilder<TState> flowBuilder)
    {
        _flowBuilder = flowBuilder;
        _loopStep = new LoopStep<TState>
        {
            Type = LoopType.DoWhile
        };

        // Create the FlowStep for the do-while loop
        var flowStep = new FlowStep
        {
            Type = StepType.DoWhile,
            LoopCondition = null, // Will be set when Until is called
            LoopSteps = []
        };
        _flowBuilder.Steps.Add(flowStep);
    }

    public IDoWhileBuilder<TState> Send<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest
    {
        // Add send step to loop
        if (_flowBuilder.Steps.Count > 0)
        {
            var lastStep = _flowBuilder.Steps[^1];
            if (lastStep.Type == StepType.DoWhile && lastStep.LoopSteps != null)
            {
                var step = new FlowStep { Type = StepType.Send, RequestFactory = factory };
                lastStep.LoopSteps.Add(step);
            }
        }
        return this;
    }

    public IDoWhileBuilder<TState, TResult> Send<TResult>(Func<TState, IRequest<TResult>> factory)
    {
        // Add send step with result to loop
        return new DoWhileBuilderWithResult<TState, TResult>(this);
    }

    public IDoWhileBuilder<TState, TResult> Query<TResult>(Func<TState, IRequest<TResult>> factory)
    {
        // Add query step to loop
        return new DoWhileBuilderWithResult<TState, TResult>(this);
    }

    public IDoWhileBuilder<TState> Publish<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent
    {
        // Add publish step to loop
        return this;
    }

    public IFlowBuilder<TState> Until(Expression<Func<TState, bool>> condition)
    {
        _loopStep.Condition = condition;
        // Apply loop step to flow builder
        return _flowBuilder;
    }
}

/// <summary>
/// Implementation of do-while loop builder with result.
/// </summary>
internal class DoWhileBuilderWithResult<TState, TResult> : IDoWhileBuilder<TState, TResult> where TState : class, IFlowState
{
    private readonly DoWhileBuilder<TState> _doWhileBuilder;

    public DoWhileBuilderWithResult(DoWhileBuilder<TState> doWhileBuilder)
    {
        _doWhileBuilder = doWhileBuilder;
    }

    public IDoWhileBuilder<TState> Into(Expression<Func<TState, TResult>> property)
    {
        // Set result into property
        return _doWhileBuilder;
    }
}

/// <summary>
/// Implementation of repeat loop builder.
/// </summary>
internal class RepeatBuilder<TState> : IRepeatBuilder<TState> where TState : class, IFlowState
{
    private readonly FlowBuilder<TState> _flowBuilder;
    private readonly LoopStep<TState> _loopStep;

    public RepeatBuilder(FlowBuilder<TState> flowBuilder, int times)
    {
        _flowBuilder = flowBuilder;
        _loopStep = new LoopStep<TState>
        {
            Type = LoopType.Repeat,
            FixedTimes = times
        };

        // Create the FlowStep for the repeat loop
        var flowStep = new FlowStep
        {
            Type = StepType.Repeat,
            RepeatCount = times,
            LoopSteps = []
        };
        _flowBuilder.Steps.Add(flowStep);
    }

    public RepeatBuilder(FlowBuilder<TState> flowBuilder, Func<TState, int> timesSelector)
    {
        _flowBuilder = flowBuilder;
        _loopStep = new LoopStep<TState>
        {
            Type = LoopType.Repeat,
            TimesSelector = null // Will be set when EndRepeat is called
        };

        // Create the FlowStep for the repeat loop
        var flowStep = new FlowStep
        {
            Type = StepType.Repeat,
            RepeatCountSelector = timesSelector,
            LoopSteps = []
        };
        _flowBuilder.Steps.Add(flowStep);
    }

    public IRepeatBuilder<TState> Send<TRequest>(Func<TState, TRequest> factory) where TRequest : IRequest
    {
        // Add send step to loop
        if (_flowBuilder.Steps.Count > 0)
        {
            var lastStep = _flowBuilder.Steps[^1];
            if (lastStep.Type == StepType.Repeat && lastStep.LoopSteps != null)
            {
                var step = new FlowStep { Type = StepType.Send, RequestFactory = factory };
                lastStep.LoopSteps.Add(step);
            }
        }
        return this;
    }

    public IRepeatBuilder<TState, TResult> Send<TResult>(Func<TState, IRequest<TResult>> factory)
    {
        // Add send step with result to loop
        return new RepeatBuilderWithResult<TState, TResult>(this);
    }

    public IRepeatBuilder<TState, TResult> Query<TResult>(Func<TState, IRequest<TResult>> factory)
    {
        // Add query step to loop
        return new RepeatBuilderWithResult<TState, TResult>(this);
    }

    public IRepeatBuilder<TState> Publish<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent
    {
        // Add publish step to loop
        return this;
    }

    public IRepeatBuilder<TState> BreakIf(Expression<Func<TState, bool>> condition)
    {
        _loopStep.BreakCondition = condition;
        return this;
    }

    public IFlowBuilder<TState> EndRepeat()
    {
        // Apply loop step to flow builder
        return _flowBuilder;
    }
}

/// <summary>
/// Implementation of repeat loop builder with result.
/// </summary>
internal class RepeatBuilderWithResult<TState, TResult> : IRepeatBuilder<TState, TResult> where TState : class, IFlowState
{
    private readonly RepeatBuilder<TState> _repeatBuilder;

    public RepeatBuilderWithResult(RepeatBuilder<TState> repeatBuilder)
    {
        _repeatBuilder = repeatBuilder;
    }

    public IRepeatBuilder<TState> Into(Expression<Func<TState, TResult>> property)
    {
        // Set result into property
        return _repeatBuilder;
    }
}
