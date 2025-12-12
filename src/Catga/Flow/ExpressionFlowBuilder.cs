using System.Linq.Expressions;
using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Expression-based flow builder for more flexible and powerful flow configuration.
/// </summary>
public interface IExpressionFlowBuilder<TState> where TState : class, IFlowState
{
    /// <summary>
    /// Execute steps when condition is true.
    /// </summary>
    IExpressionFlowBuilder<TState> When(Expression<Func<TState, bool>> condition);

    /// <summary>
    /// Select a value and execute steps based on it.
    /// </summary>
    IExpressionFlowBuilder<TState> Select<TValue>(
        Expression<Func<TState, TValue>> selector,
        Action<TValue, IFlowBuilder<TState>> configure);

    /// <summary>
    /// Update a property using an expression.
    /// </summary>
    IExpressionFlowBuilder<TState> Update<TValue>(
        Expression<Func<TState, TValue>> property,
        Expression<Func<TState, TValue>> valueExpression);

    /// <summary>
    /// Filter state using a predicate expression.
    /// </summary>
    IExpressionFlowBuilder<TState> Where(Expression<Func<TState, bool>> predicate);

    /// <summary>
    /// Map state to a new value and merge back.
    /// </summary>
    IExpressionFlowBuilder<TState> Map<TResult>(
        Expression<Func<TState, TResult>> mapper,
        Expression<Func<TState, TResult, TState>> merger);

    /// <summary>
    /// Execute steps for each item in a collection selected by expression.
    /// </summary>
    IExpressionFlowBuilder<TState> ForEachExpression<TItem>(
        Expression<Func<TState, IEnumerable<TItem>>> collectionSelector,
        Action<TItem, IFlowBuilder<TState>> configure);

    /// <summary>
    /// Conditionally include steps based on expression.
    /// </summary>
    IExpressionFlowBuilder<TState> IfPresent<TValue>(
        Expression<Func<TState, TValue?>> selector,
        Action<IFlowBuilder<TState>, TValue> configure)
        where TValue : class;

    /// <summary>
    /// Execute steps with access to expression-evaluated context.
    /// </summary>
    IExpressionFlowBuilder<TState> WithContext(
        Expression<Func<TState, object>> contextSelector,
        Action<IFlowBuilder<TState>> configure);

    /// <summary>
    /// End the expression flow builder and return to regular flow builder.
    /// </summary>
    IFlowBuilder<TState> End();
}

/// <summary>
/// Implementation of expression-based flow builder.
/// </summary>
internal class ExpressionFlowBuilder<TState> : IExpressionFlowBuilder<TState> where TState : class, IFlowState
{
    private readonly FlowBuilder<TState> _flowBuilder;
    private readonly List<ExpressionStep<TState>> _expressionSteps = new();

    public ExpressionFlowBuilder(FlowBuilder<TState> flowBuilder)
    {
        _flowBuilder = flowBuilder;
    }

    public IExpressionFlowBuilder<TState> When(Expression<Func<TState, bool>> condition)
    {
        _expressionSteps.Add(new ExpressionStep<TState>
        {
            Type = ExpressionStepType.When,
            Condition = condition
        });
        return this;
    }

    public IExpressionFlowBuilder<TState> Select<TValue>(
        Expression<Func<TState, TValue>> selector,
        Action<TValue, IFlowBuilder<TState>> configure)
    {
        _expressionSteps.Add(new ExpressionStep<TState>
        {
            Type = ExpressionStepType.Select,
            Selector = selector,
            ConfigureAction = configure
        });
        return this;
    }

    public IExpressionFlowBuilder<TState> Update<TValue>(
        Expression<Func<TState, TValue>> property,
        Expression<Func<TState, TValue>> valueExpression)
    {
        _expressionSteps.Add(new ExpressionStep<TState>
        {
            Type = ExpressionStepType.Update,
            Property = property,
            ValueExpression = valueExpression
        });
        return this;
    }

    public IExpressionFlowBuilder<TState> Where(Expression<Func<TState, bool>> predicate)
    {
        _expressionSteps.Add(new ExpressionStep<TState>
        {
            Type = ExpressionStepType.Where,
            Condition = predicate
        });
        return this;
    }

    public IExpressionFlowBuilder<TState> Map<TResult>(
        Expression<Func<TState, TResult>> mapper,
        Expression<Func<TState, TResult, TState>> merger)
    {
        _expressionSteps.Add(new ExpressionStep<TState>
        {
            Type = ExpressionStepType.Map,
            Mapper = mapper,
            Merger = merger
        });
        return this;
    }

    public IExpressionFlowBuilder<TState> ForEachExpression<TItem>(
        Expression<Func<TState, IEnumerable<TItem>>> collectionSelector,
        Action<TItem, IFlowBuilder<TState>> configure)
    {
        _expressionSteps.Add(new ExpressionStep<TState>
        {
            Type = ExpressionStepType.ForEach,
            CollectionSelector = collectionSelector,
            ConfigureAction = configure
        });
        return this;
    }

    public IExpressionFlowBuilder<TState> IfPresent<TValue>(
        Expression<Func<TState, TValue?>> selector,
        Action<IFlowBuilder<TState>, TValue> configure)
        where TValue : class
    {
        _expressionSteps.Add(new ExpressionStep<TState>
        {
            Type = ExpressionStepType.IfPresent,
            Selector = selector,
            ConfigureAction = configure
        });
        return this;
    }

    public IExpressionFlowBuilder<TState> WithContext(
        Expression<Func<TState, object>> contextSelector,
        Action<IFlowBuilder<TState>> configure)
    {
        _expressionSteps.Add(new ExpressionStep<TState>
        {
            Type = ExpressionStepType.WithContext,
            ContextSelector = contextSelector,
            ConfigureAction = configure
        });
        return this;
    }

    public IFlowBuilder<TState> End()
    {
        // Compile and apply all expression steps to the flow builder
        foreach (var step in _expressionSteps)
        {
            ApplyExpressionStep(step);
        }
        return _flowBuilder;
    }

    private void ApplyExpressionStep(ExpressionStep<TState> step)
    {
        switch (step.Type)
        {
            case ExpressionStepType.When:
                // When condition is true, execute subsequent steps
                if (step.Condition is LambdaExpression whenLambda)
                {
                    var whenFunc = (Func<TState, bool>)whenLambda.Compile();
                    _flowBuilder.If(whenFunc);
                }
                break;

            case ExpressionStepType.Where:
                // Filter based on predicate
                if (step.Condition is LambdaExpression whereLambda)
                {
                    var whereFunc = (Func<TState, bool>)whereLambda.Compile();
                    _flowBuilder.If(whereFunc);
                }
                break;

            case ExpressionStepType.Select:
                // Select a value and pass to configure action
                // This would need to be handled in the executor
                break;

            case ExpressionStepType.Update:
                // Update property with expression value
                // This would need to be handled in the executor
                break;

            case ExpressionStepType.Map:
                // Map and merge state
                // This would need to be handled in the executor
                break;

            case ExpressionStepType.ForEach:
                // ForEach with expression
                // This would need to be handled in the executor
                break;

            case ExpressionStepType.IfPresent:
                // IfPresent with null check
                // This would need to be handled in the executor
                break;

            case ExpressionStepType.WithContext:
                // WithContext for accessing computed values
                // This would need to be handled in the executor
                break;
        }
    }
}

/// <summary>
/// Expression step type enumeration.
/// </summary>
internal enum ExpressionStepType
{
    When,
    Where,
    Select,
    Update,
    Map,
    ForEach,
    IfPresent,
    WithContext
}

/// <summary>
/// Internal representation of an expression step.
/// </summary>
internal class ExpressionStep<TState> where TState : class, IFlowState
{
    public ExpressionStepType Type { get; set; }
    public Expression? Condition { get; set; }
    public Expression? Selector { get; set; }
    public Expression? Property { get; set; }
    public Expression? ValueExpression { get; set; }
    public Expression? Mapper { get; set; }
    public Expression? Merger { get; set; }
    public Expression? CollectionSelector { get; set; }
    public Expression? ContextSelector { get; set; }
    public object? ConfigureAction { get; set; }
}
