using Catga.EventSourcing;
using Catga.Resilience;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Stores;

/// <summary>Redis-based subscription store with distributed locking support.</summary>
public sealed class RedisSubscriptionStore(IConnectionMultiplexer redis, IResiliencePipelineProvider provider, string prefix = "subscription:", TimeSpan? lockExpiry = null) : ISubscriptionStore
{
    private readonly TimeSpan _lockExpiry = lockExpiry ?? TimeSpan.FromSeconds(30);

    public async ValueTask SaveAsync(PersistentSubscription subscription, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async _ =>
        {
            var db = redis.GetDatabase();
            await db.HashSetAsync(prefix + subscription.Name, [
                new("name", subscription.Name), new("streamPattern", subscription.StreamPattern), new("position", subscription.Position),
                new("eventTypeFilter", string.Join(",", subscription.EventTypeFilter)), new("processedCount", subscription.ProcessedCount),
                new("lastProcessedAt", subscription.LastProcessedAt?.Ticks ?? 0), new("createdAt", subscription.CreatedAt.Ticks)
            ]);
            await db.SetAddAsync(prefix + "index", subscription.Name);
        }, ct);

    public async ValueTask<PersistentSubscription?> LoadAsync(string name, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async _ =>
        {
            var entries = await redis.GetDatabase().HashGetAllAsync(prefix + name);
            if (entries.Length == 0) return null;
            var d = entries.ToDictionary(e => (string)e.Name!, e => e.Value);
            var sub = new PersistentSubscription((string)d["name"]!, (string)d["streamPattern"]!)
            {
                Position = (long)d["position"], ProcessedCount = (long)d["processedCount"],
                LastProcessedAt = (long)d["lastProcessedAt"] > 0 ? new DateTime((long)d["lastProcessedAt"], DateTimeKind.Utc) : null
            };
            var filter = (string?)d["eventTypeFilter"];
            if (!string.IsNullOrEmpty(filter)) sub.EventTypeFilter = [.. filter.Split(',', StringSplitOptions.RemoveEmptyEntries)];
            return sub;
        }, ct);

    public async ValueTask DeleteAsync(string name, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async _ => { var db = redis.GetDatabase(); await db.KeyDeleteAsync(prefix + name); await db.SetRemoveAsync(prefix + "index", name); }, ct);

    public async ValueTask<IReadOnlyList<PersistentSubscription>> ListAsync(CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async _ =>
        {
            var names = await redis.GetDatabase().SetMembersAsync(prefix + "index");
            var subs = new List<PersistentSubscription>();
            foreach (var n in names) { var s = await LoadAsync((string)n!, ct); if (s != null) subs.Add(s); }
            return (IReadOnlyList<PersistentSubscription>)subs;
        }, ct);

    public async ValueTask<bool> TryAcquireLockAsync(string subscriptionName, string consumerId, CancellationToken ct = default)
        // No retry for lock operations - they are not idempotent
        => await provider.ExecutePersistenceNoRetryAsync(async _ => await redis.GetDatabase().StringSetAsync(prefix + "lock:" + subscriptionName, consumerId, _lockExpiry, When.NotExists), ct);

    public async ValueTask ReleaseLockAsync(string subscriptionName, string consumerId, CancellationToken ct = default)
        // No retry for lock release - should be atomic
        => await provider.ExecutePersistenceNoRetryAsync(async _ =>
        {
            var db = redis.GetDatabase(); var lockKey = prefix + "lock:" + subscriptionName;
            if (await db.StringGetAsync(lockKey) == consumerId) await db.KeyDeleteAsync(lockKey);
        }, ct);
}
