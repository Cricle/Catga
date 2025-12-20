using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Resilience;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;

namespace Catga.Persistence.Nats;

/// <summary>NATS KV-based subscription store.</summary>
public sealed class NatsSubscriptionStore(INatsConnection nats, IMessageSerializer serializer, IResiliencePipelineProvider provider, string bucketName = "subscriptions") : ISubscriptionStore
{
    private INatsKVStore? _kvStore;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private async ValueTask EnsureInitializedAsync(CancellationToken ct)
    {
        if (_kvStore != null) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_kvStore != null) return;
            var kv = new NatsKVContext(new NatsJSContext(nats));
            try { _kvStore = await kv.GetStoreAsync(bucketName, ct); }
            catch (NatsKVException) { _kvStore = await kv.CreateStoreAsync(new NatsKVConfig(bucketName), ct); }
        }
        finally { _initLock.Release(); }
    }

    public async ValueTask SaveAsync(PersistentSubscription subscription, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);
            var data = new SubscriptionData { Name = subscription.Name, StreamPattern = subscription.StreamPattern, Position = subscription.Position, EventTypeFilter = subscription.EventTypeFilter, ProcessedCount = subscription.ProcessedCount, LastProcessedAtTicks = subscription.LastProcessedAt?.Ticks ?? 0, CreatedAtTicks = subscription.CreatedAt.Ticks };
            await _kvStore!.PutAsync(subscription.Name, serializer.Serialize(data, typeof(SubscriptionData)), cancellationToken: ct);
        }, ct);

    public async ValueTask<PersistentSubscription?> LoadAsync(string name, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);
            try
            {
                var entry = await _kvStore!.GetEntryAsync<byte[]>(name, cancellationToken: ct);
                if (entry.Value == null) return null;
                var data = (SubscriptionData?)serializer.Deserialize(entry.Value, typeof(SubscriptionData));
                if (data == null) return null;
                return new PersistentSubscription(data.Name, data.StreamPattern) { Position = data.Position, EventTypeFilter = data.EventTypeFilter, ProcessedCount = data.ProcessedCount, LastProcessedAt = data.LastProcessedAtTicks > 0 ? new(data.LastProcessedAtTicks, DateTimeKind.Utc) : null };
            }
            catch (NatsKVKeyNotFoundException) { return null; }
        }, ct);

    public async ValueTask DeleteAsync(string name, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async _ => { await EnsureInitializedAsync(ct); try { await _kvStore!.DeleteAsync(name, cancellationToken: ct); } catch (NatsKVKeyNotFoundException) { } }, ct);

    public async ValueTask<IReadOnlyList<PersistentSubscription>> ListAsync(CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);
            var subs = new List<PersistentSubscription>();
            await foreach (var key in _kvStore!.GetKeysAsync(cancellationToken: ct))
            {
                if (key.StartsWith("lock:")) continue;
                var sub = await LoadAsync(key, ct);
                if (sub != null) subs.Add(sub);
            }
            return (IReadOnlyList<PersistentSubscription>)subs;
        }, ct);

    public async ValueTask<bool> TryAcquireLockAsync(string subscriptionName, string consumerId, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);
            try { await _kvStore!.CreateAsync($"lock:{subscriptionName}", System.Text.Encoding.UTF8.GetBytes(consumerId), cancellationToken: ct); return true; }
            catch (NatsKVCreateException) { return false; }
        }, ct);

    public async ValueTask ReleaseLockAsync(string subscriptionName, string consumerId, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);
            var lockKey = $"lock:{subscriptionName}";
            try
            {
                var entry = await _kvStore!.GetEntryAsync<byte[]>(lockKey, cancellationToken: ct);
                if (entry.Value != null && System.Text.Encoding.UTF8.GetString(entry.Value) == consumerId)
                    await _kvStore.DeleteAsync(lockKey, cancellationToken: ct);
            }
            catch (NatsKVKeyNotFoundException) { }
        }, ct);

    private sealed class SubscriptionData { public string Name { get; set; } = ""; public string StreamPattern { get; set; } = ""; public long Position { get; set; } public List<string> EventTypeFilter { get; set; } = []; public long ProcessedCount { get; set; } public long LastProcessedAtTicks { get; set; } public long CreatedAtTicks { get; set; } }
}
