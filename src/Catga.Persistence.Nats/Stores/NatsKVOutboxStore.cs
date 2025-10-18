using Catga.Messages;
using Catga.Outbox;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using System.Text.Json;

namespace Catga.Persistence.Stores;

/// <summary>
/// NATS KV-based Outbox store for reliable message publishing
/// </summary>
public sealed class NatsKVOutboxStore : IOutboxStore, IAsyncDisposable
{
    private readonly INatsJSContext _jetStream;
    private readonly string _bucketName;
    private INatsKVStore? _kvStore;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public NatsKVOutboxStore(
        INatsJSContext jetStream,
        string? bucketName = null)
    {
        _jetStream = jetStream ?? throw new ArgumentNullException(nameof(jetStream));
        _bucketName = bucketName ?? "catga-outbox";
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            var config = new NatsKVConfig(_bucketName)
            {
                History = 1,
                MaxBucketSize = -1
            };

            _kvStore = await _jetStream.CreateKeyValueStoreAsync(config, cancellationToken);
            _initialized = true;
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 400)
        {
            _kvStore = await _jetStream.GetKeyValueStoreAsync(_bucketName, cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await EnsureInitializedAsync(cancellationToken);

        var key = $"outbox:{message.Id}";
        var data = JsonSerializer.SerializeToUtf8Bytes(message);

        await _kvStore!.PutAsync(key, data, cancellationToken: cancellationToken);
    }

    public async Task<List<OutboxMessage>> GetPendingAsync(
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var messages = new List<OutboxMessage>();
        var keys = _kvStore!.GetKeysAsync(cancellationToken: cancellationToken);

        await foreach (var key in keys.WithCancellation(cancellationToken))
        {
            if (messages.Count >= batchSize) break;
            if (!key.StartsWith("outbox:")) continue;

            try
            {
                var entry = await _kvStore.GetEntryAsync<byte[]>(key, cancellationToken: cancellationToken);
                if (entry?.Value != null)
                {
                    var message = JsonSerializer.Deserialize<OutboxMessage>(entry.Value);
                    if (message != null && !message.IsProcessed)
                    {
                        messages.Add(message);
                    }
                }
            }
            catch (NatsKVKeyNotFoundException)
            {
                // Message was deleted
            }
        }

        return messages;
    }

    public async Task MarkAsProcessedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var key = $"outbox:{messageId}";

        try
        {
            // Delete the message from outbox (it's been processed)
            await _kvStore!.DeleteAsync(key, cancellationToken: cancellationToken);
        }
        catch (NatsKVKeyNotFoundException)
        {
            // Already deleted
        }
    }

    public ValueTask DisposeAsync()
    {
        _initLock.Dispose();
        return ValueTask.CompletedTask;
    }
}

