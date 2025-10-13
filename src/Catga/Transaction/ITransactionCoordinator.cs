using System.Diagnostics.CodeAnalysis;

namespace Catga.Transaction;

/// <summary>Transaction coordinator - executes distributed transactions</summary>
public interface ITransactionCoordinator
{
    /// <summary>Start a distributed transaction</summary>
    Task<TransactionResult> StartAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TContext>(
        IDistributedTransaction<TContext> transaction,
        TContext context,
        TransactionOptions? options = null,
        CancellationToken cancellationToken = default)
        where TContext : class;

    /// <summary>Get transaction snapshot</summary>
    Task<TransactionSnapshot?> GetSnapshotAsync(
        string transactionId,
        CancellationToken cancellationToken = default);

    /// <summary>Get all incomplete transactions (for recovery)</summary>
    Task<IReadOnlyList<TransactionSnapshot>> GetIncompleteTransactionsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>Transaction execution result</summary>
public readonly struct TransactionResult
{
    public bool IsSuccess { get; init; }
    public TransactionStatus Status { get; init; }
    public string? Error { get; init; }
    public Exception? Exception { get; init; }
    public TransactionSnapshot? Snapshot { get; init; }

    public static TransactionResult Success(TransactionSnapshot snapshot)
        => new() { IsSuccess = true, Status = TransactionStatus.Completed, Snapshot = snapshot };

    public static TransactionResult Compensated(TransactionSnapshot snapshot)
        => new() { IsSuccess = false, Status = TransactionStatus.Compensated, Snapshot = snapshot };

    public static TransactionResult Failure(string error, TransactionSnapshot? snapshot = null, Exception? exception = null)
        => new() { IsSuccess = false, Status = TransactionStatus.Failed, Error = error, Exception = exception, Snapshot = snapshot };

    public static TransactionResult TimedOut(TransactionSnapshot snapshot)
        => new() { IsSuccess = false, Status = TransactionStatus.TimedOut, Snapshot = snapshot };
}

/// <summary>Transaction execution options</summary>
public record TransactionOptions
{
    /// <summary>Transaction timeout</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Enable automatic compensation on failure</summary>
    public bool AutoCompensate { get; init; } = true;

    /// <summary>Maximum retry attempts per step</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>Retry delay</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Enable event sourcing (store all events)</summary>
    public bool EnableEventSourcing { get; init; } = true;
}

