using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Resilience;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;

namespace Catga.Persistence.Nats;

/// <summary>
/// NATS KV-based subscription store.
/// </summary>
public sealed class NatsSubscriptionStore : ISubscriptionStore
{
    private readonly INatsConnection _nats;
    private readonly IMessageSerializer _serializer;
    private readonly IResiliencePipelineProvider _provider;
    private readonly string _bucketName;
    private INatsKVContext? _kv;
    private INatsKVStore? _kvStore;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly TimeSpan _lockExpiry;

    public NatsSubscriptionStore(
        INatsConnection nats,
        IMessageSerializer serializer,
        IResiliencePipelineProvider provider,
        string bucketName = "subscriptions",
        TimeSpan? lockExpiry = null)
    {
        _nats = nats;
        _serializer = serializer;
        _provider = provider;
        _bucketName = bucketName;
        _lockExpiry = lockExpiry ?? TimeSpan.FromSeconds(30);
    }

    private async ValueTask EnsureInitializedAsync(CancellationToken ct)
    {
        if (_kvStore != null) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_kvStore != null) return;

            _kv = new NatsKVContext(new NatsJSContext(_nats));
            try
            {
                _kvStore = await _kv.GetStoreAsync(_bucketName, ct);
            }
            catch (NatsKVException)
            {
                _kvStore = await _kv.CreateStoreAsync(new NatsKVConfig(_bucketName), ct);
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask SaveAsync(PersistentSubscription subscription, CancellationToken ct = default)
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);

            var data = new SubscriptionData
            {
                Name = subscription.Name,
                StreamPattern = subscription.StreamPattern,
                Position = subscription.Position,
                EventTypeFilter = subscription.EventTypeFilter,
                ProcessedCount = subscription.ProcessedCount,
                LastProcessedAtTicks = subscription.LastProcessedAt?.Ticks ?? 0,
                CreatedAtTicks = subscription.CreatedAt.Ticks
            };

            var bytes = _serializer.Serialize(data, typeof(SubscriptionData));
            await _kvStore!.PutAsync(subscription.Name, bytes, cancellationToken: ct);
        }, ct);
    }

    public async ValueTask<PersistentSubscription?> LoadAsync(string name, CancellationToken ct = default)
    {
        return await _provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);

            try
            {
                var entry = await _kvStore!.GetEntryAsync<byte[]>(name, cancellationToken: ct);
                if (entry.Value == null) return null;

                var data = (SubscriptionData?)_serializer.Deserialize(entry.Value, typeof(SubscriptionData));
                if (data == null) return null;

                return new PersistentSubscription(data.Name, data.StreamPattern)
                {
                    Position = data.Position,
                    EventTypeFilter = data.EventTypeFilter,
                    ProcessedCount = data.ProcessedCount,
                    LastProcessedAt = data.LastProcessedAtTicks > 0
                        ? new DateTime(data.LastProcessedAtTicks, DateTimeKind.Utc)
                        : null
                };
            }
            catch (NatsKVKeyNotFoundException)
            {
                return null;
            }
        }, ct);
    }

    public async ValueTask DeleteAsync(string name, CancellationToken ct = default)
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);
            try
            {
                await _kvStore!.DeleteAsync(name, cancellationToken: ct);
            }
            catch (NatsKVKeyNotFoundException) { /* Already deleted, ignore */ }
        }, ct);
    }

    public async ValueTask<IReadOnlyList<PersistentSubscription>> ListAsync(CancellationToken ct = default)
    {
        return await _provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);
            var subscriptions = new List<PersistentSubscription>();

            await foreach (var key in _kvStore!.GetKeysAsync(cancellationToken: ct))
            {
                if (key.StartsWith("lock:")) continue; // Skip lock keys

                var sub = await LoadAsync(key, ct);
                if (sub != null) subscriptions.Add(sub);
            }

            return (IReadOnlyList<PersistentSubscription>)subscriptions;
        }, ct);
    }

    public async ValueTask<bool> TryAcquireLockAsync(string subscriptionName, string consumerId, CancellationToken ct = default)
    {
        return await _provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);
            var lockKey = $"lock:{subscriptionName}";

            try
            {
                // Try to create lock (will fail if exists)
                await _kvStore!.CreateAsync(lockKey, System.Text.Encoding.UTF8.GetBytes(consumerId), cancellationToken: ct);
                return true;
            }
            catch (NatsKVCreateException)
            {
                return false;
            }
        }, ct);
    }

    public async ValueTask ReleaseLockAsync(string subscriptionName, string consumerId, CancellationToken ct = default)
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);
            var lockKey = $"lock:{subscriptionName}";

            try
            {
                var entry = await _kvStore!.GetEntryAsync<byte[]>(lockKey, cancellationToken: ct);
                if (entry.Value != null)
                {
                    var owner = System.Text.Encoding.UTF8.GetString(entry.Value);
                    if (owner == consumerId)
                    {
                        await _kvStore.DeleteAsync(lockKey, cancellationToken: ct);
                    }
                }
            }
            catch (NatsKVKeyNotFoundException) { /* Already deleted, ignore */ }
        }, ct);
    }

    private sealed class SubscriptionData
    {
        public string Name { get; set; } = "";
        public string StreamPattern { get; set; } = "";
        public long Position { get; set; }
        public List<string> EventTypeFilter { get; set; } = [];
        public long ProcessedCount { get; set; }
        public long LastProcessedAtTicks { get; set; }
        public long CreatedAtTicks { get; set; }
    }
}
