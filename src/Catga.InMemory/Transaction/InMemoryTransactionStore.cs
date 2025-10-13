using System.Collections.Concurrent;
using Catga.Transaction;

namespace Catga.InMemory.Transaction;

/// <summary>In-memory transaction store - for development and testing</summary>
public sealed class InMemoryTransactionStore : ITransactionStore
{
    private readonly ConcurrentDictionary<string, TransactionSnapshot> _snapshots = new();
    private readonly ConcurrentDictionary<string, List<TransactionEvent>> _events = new();

    public Task SaveSnapshotAsync(TransactionSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        _snapshots[snapshot.TransactionId] = snapshot;
        return Task.CompletedTask;
    }

    public Task<TransactionSnapshot?> LoadSnapshotAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        _snapshots.TryGetValue(transactionId, out var snapshot);
        return Task.FromResult(snapshot);
    }

    public Task AppendEventAsync(string transactionId, TransactionEvent @event, CancellationToken cancellationToken = default)
    {
        var events = _events.GetOrAdd(transactionId, _ => new List<TransactionEvent>());
        lock (events)
        {
            events.Add(@event);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TransactionEvent>> GetEventsAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        if (_events.TryGetValue(transactionId, out var events))
        {
            lock (events)
            {
                return Task.FromResult<IReadOnlyList<TransactionEvent>>(events.ToList());
            }
        }
        return Task.FromResult<IReadOnlyList<TransactionEvent>>(Array.Empty<TransactionEvent>());
    }

    public Task DeleteAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        _snapshots.TryRemove(transactionId, out _);
        _events.TryRemove(transactionId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TransactionSnapshot>> GetIncompleteAsync(CancellationToken cancellationToken = default)
    {
        var incomplete = _snapshots.Values
            .Where(s => s.Status == TransactionStatus.Running || s.Status == TransactionStatus.Compensating)
            .ToList();
        return Task.FromResult<IReadOnlyList<TransactionSnapshot>>(incomplete);
    }
}

