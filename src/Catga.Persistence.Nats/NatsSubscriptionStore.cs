using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Resilience;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;
using System.Text;

namespace Catga.Persistence.Nats;

/// <summary>NATS KV-based subscription store.</summary>
public sealed class NatsSubscriptionStore(INatsConnection nats, IMessageSerializer serializer, IResiliencePipelineProvider provider, string bucketName = "subscriptions", TimeSpan? lockExpiry = null) : ISubscriptionStore
{
    private readonly TimeSpan _lockExpiry = lockExpiry ?? TimeSpan.FromSeconds(30);
    private volatile INatsKVStore? _kvStore;
    private Task? _initTask;

    private async ValueTask EnsureInitializedAsync(CancellationToken ct)
    {
        if (_kvStore != null) return;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var existing = Interlocked.CompareExchange(ref _initTask, tcs.Task, null);
        if (existing != null)
        {
            await existing.WaitAsync(ct).ConfigureAwait(false);
            return;
        }

        try
        {
            var kv = new NatsKVContext(new NatsJSContext(nats));
            try { _kvStore = await kv.GetStoreAsync(bucketName, ct); }
            catch (NatsKVException) { _kvStore = await kv.CreateStoreAsync(new NatsKVConfig(bucketName), ct); }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404) { _kvStore = await kv.CreateStoreAsync(new NatsKVConfig(bucketName), ct); }
            tcs.SetResult();
        }
        catch (Exception ex)
        {
            _ = Interlocked.Exchange(ref _initTask, null);
            tcs.SetException(ex);
            throw;
        }
    }

    public async ValueTask SaveAsync(PersistentSubscription subscription, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);
            var data = new SubscriptionData { Name = subscription.Name, StreamPattern = subscription.StreamPattern, Position = subscription.Position, EventTypeFilter = subscription.EventTypeFilter, ProcessedCount = subscription.ProcessedCount, LastProcessedAtTicks = subscription.LastProcessedAt?.Ticks ?? 0, CreatedAtTicks = subscription.CreatedAt.Ticks };
            await _kvStore!.PutAsync(subscription.Name, serializer.Serialize(data), cancellationToken: ct);
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
                if (key.StartsWith("lock.", StringComparison.Ordinal)) continue;
                var sub = await LoadAsync(key, ct);
                if (sub != null) subs.Add(sub);
            }
            return (IReadOnlyList<PersistentSubscription>)subs;
        }, ct);

    public async ValueTask<bool> TryAcquireLockAsync(string subscriptionName, string consumerId, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);
            var lockKey = GetLockKey(subscriptionName);
            var lockData = new SubscriptionLockData
            {
                ConsumerId = consumerId,
                ExpiresAtTicks = DateTime.UtcNow.Add(_lockExpiry).Ticks
            };
            var payload = SerializeLockData(lockData);

            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    await _kvStore!.CreateAsync(lockKey, payload, cancellationToken: ct);
                    return true;
                }
                catch (NatsKVCreateException)
                {
                    try
                    {
                        var entry = await _kvStore!.GetEntryAsync<byte[]>(lockKey, cancellationToken: ct);
                        var currentLock = entry.Value is { Length: > 0 }
                            ? DeserializeLockData(entry.Value)
                            : null;

                        if (currentLock is { ExpiresAtTicks: > 0 } && currentLock.ExpiresAtTicks > DateTime.UtcNow.Ticks)
                            return false;

                        await _kvStore.UpdateAsync(lockKey, payload, entry.Revision, cancellationToken: ct);
                        return true;
                    }
                    catch (NatsKVWrongLastRevisionException)
                    {
                        continue;
                    }
                    catch (NatsKVKeyNotFoundException)
                    {
                        continue;
                    }
                    catch (NatsKVKeyDeletedException)
                    {
                        continue;
                    }
                }
            }

            return false;
        }, ct);

    public async ValueTask ReleaseLockAsync(string subscriptionName, string consumerId, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);
            var lockKey = GetLockKey(subscriptionName);
            try
            {
                var entry = await _kvStore!.GetEntryAsync<byte[]>(lockKey, cancellationToken: ct);
                var currentLock = entry.Value is { Length: > 0 }
                    ? DeserializeLockData(entry.Value)
                    : null;
                if (currentLock?.ConsumerId == consumerId)
                    await _kvStore.DeleteAsync(lockKey, cancellationToken: ct);
            }
            catch (NatsKVKeyNotFoundException) { }
            catch (NatsKVKeyDeletedException) { }
        }, ct);

    private static string GetLockKey(string subscriptionName)
        => $"lock.{Convert.ToHexString(Encoding.UTF8.GetBytes(subscriptionName))}";

    private static byte[] SerializeLockData(SubscriptionLockData data)
        => Encoding.UTF8.GetBytes($"{data.ExpiresAtTicks}:{Convert.ToHexString(Encoding.UTF8.GetBytes(data.ConsumerId))}");

    private static SubscriptionLockData? DeserializeLockData(byte[] payload)
    {
        var text = Encoding.UTF8.GetString(payload);
        var separatorIndex = text.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= text.Length - 1)
            return null;

        if (!long.TryParse(text[..separatorIndex], out var expiresAtTicks))
            return null;

        try
        {
            var consumerIdBytes = Convert.FromHexString(text[(separatorIndex + 1)..]);
            return new SubscriptionLockData
            {
                ExpiresAtTicks = expiresAtTicks,
                ConsumerId = Encoding.UTF8.GetString(consumerIdBytes)
            };
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private sealed class SubscriptionLockData
    {
        public string ConsumerId { get; set; } = "";
        public long ExpiresAtTicks { get; set; }
    }

    private sealed class SubscriptionData { public string Name { get; set; } = ""; public string StreamPattern { get; set; } = ""; public long Position { get; set; } public List<string> EventTypeFilter { get; set; } = []; public long ProcessedCount { get; set; } public long LastProcessedAtTicks { get; set; } public long CreatedAtTicks { get; set; } }
}
