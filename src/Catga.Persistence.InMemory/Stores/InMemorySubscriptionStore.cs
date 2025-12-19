using System.Collections.Concurrent;
using Catga.EventSourcing;

namespace Catga.Persistence.InMemory.Stores;

/// <summary>In-memory subscription store for development/testing.</summary>
public sealed class InMemorySubscriptionStore : ISubscriptionStore
{
    private readonly ConcurrentDictionary<string, PersistentSubscription> _subscriptions = new();
    private readonly ConcurrentDictionary<string, string> _locks = new();

    public ValueTask SaveAsync(PersistentSubscription subscription, CancellationToken ct = default) { _subscriptions[subscription.Name] = subscription; return ValueTask.CompletedTask; }
    public ValueTask<PersistentSubscription?> LoadAsync(string name, CancellationToken ct = default) => ValueTask.FromResult(_subscriptions.GetValueOrDefault(name));
    public ValueTask DeleteAsync(string name, CancellationToken ct = default) { _subscriptions.TryRemove(name, out _); return ValueTask.CompletedTask; }
    public ValueTask<IReadOnlyList<PersistentSubscription>> ListAsync(CancellationToken ct = default) => ValueTask.FromResult<IReadOnlyList<PersistentSubscription>>([.. _subscriptions.Values]);
    public ValueTask<bool> TryAcquireLockAsync(string subscriptionName, string consumerId, CancellationToken ct = default) => ValueTask.FromResult(_locks.TryAdd(subscriptionName, consumerId));
    public ValueTask ReleaseLockAsync(string subscriptionName, string consumerId, CancellationToken ct = default) { _locks.TryRemove(subscriptionName, out _); return ValueTask.CompletedTask; }
    public void Clear() { _subscriptions.Clear(); _locks.Clear(); }
}
