using Catga.Inbox;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Catga.Persistence.Stores;

/// <summary>
/// NATS KV-based Inbox store for message deduplication
/// </summary>
public sealed class NatsKVInboxStore : IInboxStore, IAsyncDisposable
{
    private readonly INatsJSContext _jetStream;
    private readonly string _bucketName;
    private readonly TimeSpan _ttl;
    private INatsKVStore? _kvStore;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public NatsKVInboxStore(
        INatsJSContext jetStream,
        string? bucketName = null,
        TimeSpan? ttl = null)
    {
        _jetStream = jetStream ?? throw new ArgumentNullException(nameof(jetStream));
        _bucketName = bucketName ?? "catga-inbox";
        _ttl = ttl ?? TimeSpan.FromHours(24);
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
                Ttl = _ttl,
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

    public async Task<bool> ExistsAsync(string messageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        await EnsureInitializedAsync(cancellationToken);

        try
        {
            var entry = await _kvStore!.GetEntryAsync<byte[]>(
                $"inbox:{messageId}",
                cancellationToken: cancellationToken);
            return entry != null;
        }
        catch (NatsKVKeyNotFoundException)
        {
            return false;
        }
    }

    public async Task AddAsync(string messageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        await EnsureInitializedAsync(cancellationToken);

        var key = $"inbox:{messageId}";
        var timestamp = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        await _kvStore!.PutAsync(key, timestamp, cancellationToken: cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _initLock.Dispose();
        return ValueTask.CompletedTask;
    }
}

