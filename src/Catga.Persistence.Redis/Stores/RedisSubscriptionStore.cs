using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Resilience;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Stores;

/// <summary>
/// Redis-based subscription store with distributed locking support.
/// </summary>
public sealed class RedisSubscriptionStore : ISubscriptionStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageSerializer _serializer;
    private readonly IResiliencePipelineProvider _provider;
    private readonly string _prefix;
    private readonly TimeSpan _lockExpiry;

    public RedisSubscriptionStore(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        IResiliencePipelineProvider provider,
        string prefix = "subscription:",
        TimeSpan? lockExpiry = null)
    {
        _redis = redis;
        _serializer = serializer;
        _provider = provider;
        _prefix = prefix;
        _lockExpiry = lockExpiry ?? TimeSpan.FromSeconds(30);
    }

    public async ValueTask SaveAsync(PersistentSubscription subscription, CancellationToken ct = default)
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            var db = _redis.GetDatabase();
            var key = _prefix + subscription.Name;

            await db.HashSetAsync(key, [
                new HashEntry("name", subscription.Name),
                new HashEntry("streamPattern", subscription.StreamPattern),
                new HashEntry("position", subscription.Position),
                new HashEntry("eventTypeFilter", string.Join(",", subscription.EventTypeFilter)),
                new HashEntry("processedCount", subscription.ProcessedCount),
                new HashEntry("lastProcessedAt", subscription.LastProcessedAt?.Ticks ?? 0),
                new HashEntry("createdAt", subscription.CreatedAt.Ticks)
            ]);

            // Add to subscription index
            await db.SetAddAsync(_prefix + "index", subscription.Name);
        }, ct);
    }

    public async ValueTask<PersistentSubscription?> LoadAsync(string name, CancellationToken ct = default)
    {
        return await _provider.ExecutePersistenceAsync(async _ =>
        {
            var db = _redis.GetDatabase();
            var key = _prefix + name;

            var entries = await db.HashGetAllAsync(key);
            if (entries.Length == 0) return null;

            var dict = entries.ToDictionary(e => (string)e.Name!, e => e.Value);

            var subscription = new PersistentSubscription(
                (string)dict["name"]!,
                (string)dict["streamPattern"]!)
            {
                Position = (long)dict["position"],
                ProcessedCount = (long)dict["processedCount"],
                LastProcessedAt = (long)dict["lastProcessedAt"] > 0
                    ? new DateTime((long)dict["lastProcessedAt"], DateTimeKind.Utc)
                    : null
            };

            var filterStr = (string?)dict["eventTypeFilter"];
            if (!string.IsNullOrEmpty(filterStr))
            {
                subscription.EventTypeFilter = filterStr.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            }

            return subscription;
        }, ct);
    }

    public async ValueTask DeleteAsync(string name, CancellationToken ct = default)
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            var db = _redis.GetDatabase();
            var key = _prefix + name;
            await db.KeyDeleteAsync(key);
            await db.SetRemoveAsync(_prefix + "index", name);
        }, ct);
    }

    public async ValueTask<IReadOnlyList<PersistentSubscription>> ListAsync(CancellationToken ct = default)
    {
        return await _provider.ExecutePersistenceAsync(async _ =>
        {
            var db = _redis.GetDatabase();
            var names = await db.SetMembersAsync(_prefix + "index");
            var subscriptions = new List<PersistentSubscription>();

            foreach (var name in names)
            {
                var sub = await LoadAsync((string)name!, ct);
                if (sub != null) subscriptions.Add(sub);
            }

            return (IReadOnlyList<PersistentSubscription>)subscriptions;
        }, ct);
    }

    public async ValueTask<bool> TryAcquireLockAsync(string subscriptionName, string consumerId, CancellationToken ct = default)
    {
        return await _provider.ExecutePersistenceAsync(async _ =>
        {
            var db = _redis.GetDatabase();
            var lockKey = _prefix + "lock:" + subscriptionName;
            return await db.StringSetAsync(lockKey, consumerId, _lockExpiry, When.NotExists);
        }, ct);
    }

    public async ValueTask ReleaseLockAsync(string subscriptionName, string consumerId, CancellationToken ct = default)
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            var db = _redis.GetDatabase();
            var lockKey = _prefix + "lock:" + subscriptionName;

            // Only release if we own the lock
            var currentOwner = await db.StringGetAsync(lockKey);
            if (currentOwner == consumerId)
            {
                await db.KeyDeleteAsync(lockKey);
            }
        }, ct);
    }
}
