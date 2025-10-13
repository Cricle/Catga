namespace Catga.Transaction;

/// <summary>Transaction state store - persists transaction snapshots and events</summary>
public interface ITransactionStore
{
    /// <summary>Save transaction snapshot</summary>
    Task SaveSnapshotAsync(TransactionSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>Load transaction snapshot</summary>
    Task<TransactionSnapshot?> LoadSnapshotAsync(string transactionId, CancellationToken cancellationToken = default);

    /// <summary>Append event to transaction</summary>
    Task AppendEventAsync(string transactionId, TransactionEvent @event, CancellationToken cancellationToken = default);

    /// <summary>Get all events for a transaction</summary>
    Task<IReadOnlyList<TransactionEvent>> GetEventsAsync(string transactionId, CancellationToken cancellationToken = default);

    /// <summary>Delete transaction data</summary>
    Task DeleteAsync(string transactionId, CancellationToken cancellationToken = default);

    /// <summary>Get incomplete transactions (for recovery)</summary>
    Task<IReadOnlyList<TransactionSnapshot>> GetIncompleteAsync(CancellationToken cancellationToken = default);
}

