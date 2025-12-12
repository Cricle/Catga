using System.Linq.Expressions;
using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// LINQ-style flow builder for advanced query operations.
/// </summary>
public interface ILinqFlowBuilder<TState> where TState : class, IFlowState
{
    /// <summary>
    /// Chain multiple requests in sequence.
    /// </summary>
    ILinqFlowBuilder<TState> Chain(
        Expression<Func<TState, IEnumerable<IRequest>>> requestsSelector);

    /// <summary>
    /// Aggregate operation over a collection.
    /// </summary>
    ILinqFlowBuilder<TState> Aggregate<TValue>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        Expression<Func<TValue, IRequest>> requestFactory);

    /// <summary>
    /// Group by operation with flow steps for each group.
    /// </summary>
    ILinqFlowBuilder<TState> GroupBy<TKey, TValue>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        Expression<Func<TValue, TKey>> keySelector,
        Action<TKey, IEnumerable<TValue>, IFlowBuilder<TState>> configure);

    /// <summary>
    /// Join operation combining two collections.
    /// </summary>
    ILinqFlowBuilder<TState> Join<TOuter, TInner, TKey, TResult>(
        Expression<Func<TState, IEnumerable<TOuter>>> outerSelector,
        Expression<Func<TState, IEnumerable<TInner>>> innerSelector,
        Expression<Func<TOuter, TKey>> outerKeySelector,
        Expression<Func<TInner, TKey>> innerKeySelector,
        Expression<Func<TOuter, TInner, TResult>> resultSelector,
        Action<TResult, IFlowBuilder<TState>> configure);

    /// <summary>
    /// Distinct operation removing duplicates.
    /// </summary>
    ILinqFlowBuilder<TState> Distinct<TValue>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector);

    /// <summary>
    /// Order by operation.
    /// </summary>
    ILinqFlowBuilder<TState> OrderBy<TValue, TKey>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        Expression<Func<TValue, TKey>> keySelector);

    /// <summary>
    /// Order by descending operation.
    /// </summary>
    ILinqFlowBuilder<TState> OrderByDescending<TValue, TKey>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        Expression<Func<TValue, TKey>> keySelector);

    /// <summary>
    /// Take operation limiting results.
    /// </summary>
    ILinqFlowBuilder<TState> Take<TValue>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        int count);

    /// <summary>
    /// Skip operation skipping results.
    /// </summary>
    ILinqFlowBuilder<TState> Skip<TValue>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        int count);

    /// <summary>
    /// Where operation filtering results.
    /// </summary>
    ILinqFlowBuilder<TState> Where<TValue>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        Expression<Func<TValue, bool>> predicate);

    /// <summary>
    /// Select operation transforming results.
    /// </summary>
    ILinqFlowBuilder<TState> Select<TValue, TResult>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        Expression<Func<TValue, TResult>> selector);

    /// <summary>
    /// SelectMany operation flattening nested collections.
    /// </summary>
    ILinqFlowBuilder<TState> SelectMany<TValue, TResult>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        Expression<Func<TValue, IEnumerable<TResult>>> resultSelector);

    /// <summary>
    /// Any operation checking if any element matches.
    /// </summary>
    ILinqFlowBuilder<TState> Any<TValue>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        Expression<Func<TValue, bool>> predicate,
        Action<IFlowBuilder<TState>, bool> configure);

    /// <summary>
    /// All operation checking if all elements match.
    /// </summary>
    ILinqFlowBuilder<TState> All<TValue>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        Expression<Func<TValue, bool>> predicate,
        Action<IFlowBuilder<TState>, bool> configure);

    /// <summary>
    /// Count operation getting collection size.
    /// </summary>
    ILinqFlowBuilder<TState> Count<TValue>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        Action<IFlowBuilder<TState>, int> configure);

    /// <summary>
    /// End LINQ flow building.
    /// </summary>
    IFlowBuilder<TState> End();
}

/// <summary>
/// LINQ operation information.
/// </summary>
internal class LinqOperation<TState> where TState : class, IFlowState
{
    public LinqOperationType Type { get; set; }
    public Expression? CollectionSelector { get; set; }
    public Expression? KeySelector { get; set; }
    public Expression? Predicate { get; set; }
    public Expression? ResultSelector { get; set; }
    public Expression? OuterSelector { get; set; }
    public Expression? InnerSelector { get; set; }
    public Expression? OuterKeySelector { get; set; }
    public Expression? InnerKeySelector { get; set; }
    public int? Count { get; set; }
    public Delegate? ConfigureAction { get; set; }
}

/// <summary>
/// LINQ operation type enumeration.
/// </summary>
internal enum LinqOperationType
{
    Chain,
    Aggregate,
    GroupBy,
    Join,
    Distinct,
    OrderBy,
    OrderByDescending,
    Take,
    Skip,
    Where,
    Select,
    SelectMany,
    Any,
    All,
    Count
}

/// <summary>
/// Implementation of LINQ flow builder.
/// </summary>
internal class LinqFlowBuilder<TState> : ILinqFlowBuilder<TState> where TState : class, IFlowState
{
    private readonly FlowBuilder<TState> _flowBuilder;
    private readonly List<LinqOperation<TState>> _operations = new();

    public LinqFlowBuilder(FlowBuilder<TState> flowBuilder)
    {
        _flowBuilder = flowBuilder;
    }

    public ILinqFlowBuilder<TState> Chain(
        Expression<Func<TState, IEnumerable<IRequest>>> requestsSelector)
    {
        _operations.Add(new LinqOperation<TState>
        {
            Type = LinqOperationType.Chain,
            CollectionSelector = requestsSelector
        });
        return this;
    }

    public ILinqFlowBuilder<TState> Aggregate<TValue>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        Expression<Func<TValue, IRequest>> requestFactory)
    {
        _operations.Add(new LinqOperation<TState>
        {
            Type = LinqOperationType.Aggregate,
            CollectionSelector = collectionSelector,
            ResultSelector = requestFactory
        });
        return this;
    }

    public ILinqFlowBuilder<TState> GroupBy<TKey, TValue>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        Expression<Func<TValue, TKey>> keySelector,
        Action<TKey, IEnumerable<TValue>, IFlowBuilder<TState>> configure)
    {
        _operations.Add(new LinqOperation<TState>
        {
            Type = LinqOperationType.GroupBy,
            CollectionSelector = collectionSelector,
            KeySelector = keySelector,
            ConfigureAction = configure
        });
        return this;
    }

    public ILinqFlowBuilder<TState> Join<TOuter, TInner, TKey, TResult>(
        Expression<Func<TState, IEnumerable<TOuter>>> outerSelector,
        Expression<Func<TState, IEnumerable<TInner>>> innerSelector,
        Expression<Func<TOuter, TKey>> outerKeySelector,
        Expression<Func<TInner, TKey>> innerKeySelector,
        Expression<Func<TOuter, TInner, TResult>> resultSelector,
        Action<TResult, IFlowBuilder<TState>> configure)
    {
        _operations.Add(new LinqOperation<TState>
        {
            Type = LinqOperationType.Join,
            OuterSelector = outerSelector,
            InnerSelector = innerSelector,
            OuterKeySelector = outerKeySelector,
            InnerKeySelector = innerKeySelector,
            ResultSelector = resultSelector,
            ConfigureAction = configure
        });
        return this;
    }

    public ILinqFlowBuilder<TState> Distinct<TValue>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector)
    {
        _operations.Add(new LinqOperation<TState>
        {
            Type = LinqOperationType.Distinct,
            CollectionSelector = collectionSelector
        });
        return this;
    }

    public ILinqFlowBuilder<TState> OrderBy<TValue, TKey>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        Expression<Func<TValue, TKey>> keySelector)
    {
        _operations.Add(new LinqOperation<TState>
        {
            Type = LinqOperationType.OrderBy,
            CollectionSelector = collectionSelector,
            KeySelector = keySelector
        });
        return this;
    }

    public ILinqFlowBuilder<TState> OrderByDescending<TValue, TKey>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        Expression<Func<TValue, TKey>> keySelector)
    {
        _operations.Add(new LinqOperation<TState>
        {
            Type = LinqOperationType.OrderByDescending,
            CollectionSelector = collectionSelector,
            KeySelector = keySelector
        });
        return this;
    }

    public ILinqFlowBuilder<TState> Take<TValue>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        int count)
    {
        _operations.Add(new LinqOperation<TState>
        {
            Type = LinqOperationType.Take,
            CollectionSelector = collectionSelector,
            Count = count
        });
        return this;
    }

    public ILinqFlowBuilder<TState> Skip<TValue>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        int count)
    {
        _operations.Add(new LinqOperation<TState>
        {
            Type = LinqOperationType.Skip,
            CollectionSelector = collectionSelector,
            Count = count
        });
        return this;
    }

    public ILinqFlowBuilder<TState> Where<TValue>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        Expression<Func<TValue, bool>> predicate)
    {
        _operations.Add(new LinqOperation<TState>
        {
            Type = LinqOperationType.Where,
            CollectionSelector = collectionSelector,
            Predicate = predicate
        });
        return this;
    }

    public ILinqFlowBuilder<TState> Select<TValue, TResult>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        Expression<Func<TValue, TResult>> selector)
    {
        _operations.Add(new LinqOperation<TState>
        {
            Type = LinqOperationType.Select,
            CollectionSelector = collectionSelector,
            ResultSelector = selector
        });
        return this;
    }

    public ILinqFlowBuilder<TState> SelectMany<TValue, TResult>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        Expression<Func<TValue, IEnumerable<TResult>>> resultSelector)
    {
        _operations.Add(new LinqOperation<TState>
        {
            Type = LinqOperationType.SelectMany,
            CollectionSelector = collectionSelector,
            ResultSelector = resultSelector
        });
        return this;
    }

    public ILinqFlowBuilder<TState> Any<TValue>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        Expression<Func<TValue, bool>> predicate,
        Action<IFlowBuilder<TState>, bool> configure)
    {
        _operations.Add(new LinqOperation<TState>
        {
            Type = LinqOperationType.Any,
            CollectionSelector = collectionSelector,
            Predicate = predicate,
            ConfigureAction = configure
        });
        return this;
    }

    public ILinqFlowBuilder<TState> All<TValue>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        Expression<Func<TValue, bool>> predicate,
        Action<IFlowBuilder<TState>, bool> configure)
    {
        _operations.Add(new LinqOperation<TState>
        {
            Type = LinqOperationType.All,
            CollectionSelector = collectionSelector,
            Predicate = predicate,
            ConfigureAction = configure
        });
        return this;
    }

    public ILinqFlowBuilder<TState> Count<TValue>(
        Expression<Func<TState, IEnumerable<TValue>>> collectionSelector,
        Action<IFlowBuilder<TState>, int> configure)
    {
        _operations.Add(new LinqOperation<TState>
        {
            Type = LinqOperationType.Count,
            CollectionSelector = collectionSelector,
            ConfigureAction = configure
        });
        return this;
    }

    public IFlowBuilder<TState> End()
    {
        // Apply all LINQ operations to the flow builder
        foreach (var operation in _operations)
        {
            ApplyLinqOperation(operation);
        }
        return _flowBuilder;
    }

    private void ApplyLinqOperation(LinqOperation<TState> operation)
    {
        // LINQ operation handling would be implemented in executor
    }
}

/// <summary>
/// Extension methods for LINQ flow building.
/// </summary>
public static class LinqFlowExtensions
{
    /// <summary>
    /// Start building with LINQ operations.
    /// </summary>
    public static ILinqFlowBuilder<TState> Linq<TState>(this IFlowBuilder<TState> builder)
        where TState : class, IFlowState
    {
        if (builder is FlowBuilder<TState> flowBuilder)
        {
            return new LinqFlowBuilder<TState>(flowBuilder);
        }
        throw new InvalidOperationException("Invalid flow builder type");
    }
}
