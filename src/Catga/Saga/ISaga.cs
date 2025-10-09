namespace Catga.Saga;

/// <summary>
/// Saga pattern for managing distributed transactions
/// </summary>
public interface ISaga
{
    /// <summary>
    /// Unique identifier for this saga
    /// </summary>
    string SagaId { get; }

    /// <summary>
    /// List of steps in the saga
    /// </summary>
    IReadOnlyList<ISagaStep> Steps { get; }
}

/// <summary>
/// A single step in a saga
/// </summary>
public interface ISagaStep
{
    /// <summary>
    /// Name of this step
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Execute the forward transaction
    /// </summary>
    ValueTask ExecuteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute the compensating transaction (rollback)
    /// </summary>
    ValueTask CompensateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Generic saga step with data
/// </summary>
public interface ISagaStep<TData> : ISagaStep
{
    /// <summary>
    /// Execute the forward transaction with data
    /// </summary>
    ValueTask<TData> ExecuteAsync(TData data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute the compensating transaction with data
    /// </summary>
    ValueTask CompensateAsync(TData data, CancellationToken cancellationToken = default);
}

