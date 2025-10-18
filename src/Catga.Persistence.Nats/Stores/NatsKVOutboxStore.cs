using Catga.Outbox;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Client.KeyValueStore;
using System.Text.Json;

namespace Catga.Persistence.Stores;

/// <summary>
/// NATS KV-based outbox store for reliable message publishing
/// </summary>
public sealed class NatsKVOutboxStore : IOutboxStore, IAsyncDisposable
{
    private readonly INatsConnection _connection;
    private readonly string _bucketName;
    private NatsKVContext? _kvContext;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public NatsKVOutboxStore(INatsConnection connection, string? bucketName = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _bucketName = bucketName ?? "catga-outbox";
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            var jsContext = new NatsJSContext(_connection);
            var config = new NatsKVConfig(_bucketName) { History = 1 };

            try
            {
                await jsContext.CreateKeyValue(config, cancellationToken);
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 400)
            {
                // Bucket already exists
            }

            _kvContext = new NatsKVContext(jsContext, _bucketName);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await EnsureInitializedAsync(cancellationToken);

        var key = $"outbox:{message.MessageId}";
        var data = JsonSerializer.SerializeToUtf8Bytes(message);

        await _kvContext!.PutAsync(key, data, cancellationToken: cancellationToken);
    }

    public async ValueTask<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var messages = new List<OutboxMessage>();
        
        // Get all keys with outbox prefix
        await foreach (var key in _kvContext!.GetKeysAsync(cancellationToken: cancellationToken))
        {
            if (messages.Count >= maxCount) break;
            if (!key.StartsWith("outbox:")) continue;

            try
            {
                var entry = await _kvContext.GetEntryAsync<byte[]>(key, cancellationToken: cancellationToken);
                if (entry?.Value != null)
                {
                    var message = JsonSerializer.Deserialize<OutboxMessage>(entry.Value);
                    if (message != null && 
                        message.Status == OutboxStatus.Pending && 
                        message.RetryCount < message.MaxRetries)
                    {
                        messages.Add(message);
                    }
                }
            }
            catch (NatsKVKeyNotFoundException)
            {
                // Key was deleted
            }
        }

        return messages.OrderBy(m => m.CreatedAt).Take(maxCount).ToList();
    }

    public async ValueTask MarkAsPublishedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var key = $"outbox:{messageId}";

        try
        {
            // Delete the message from outbox (it's been published)
            await _kvContext!.DeleteAsync(key, cancellationToken: cancellationToken);
        }
        catch (NatsKVKeyNotFoundException)
        {
            // Already deleted
        }
    }

    public async ValueTask MarkAsFailedAsync(
        string messageId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var key = $"outbox:{messageId}";

        try
        {
            var entry = await _kvContext!.GetEntryAsync<byte[]>(key, cancellationToken: cancellationToken);
            if (entry?.Value != null)
            {
                var message = JsonSerializer.Deserialize<OutboxMessage>(entry.Value);
                if (message != null)
                {
                    message.RetryCount++;
                    message.LastError = errorMessage;
                    message.Status = message.RetryCount >= message.MaxRetries 
                        ? OutboxStatus.Failed 
                        : OutboxStatus.Pending;

                    var data = JsonSerializer.SerializeToUtf8Bytes(message);
                    await _kvContext.PutAsync(key, data, cancellationToken: cancellationToken);
                }
            }
        }
        catch (NatsKVKeyNotFoundException)
        {
            // Message was deleted
        }
    }

    public async ValueTask DeletePublishedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var cutoff = DateTime.UtcNow - retentionPeriod;
        var keysToDelete = new List<string>();

        await foreach (var key in _kvContext!.GetKeysAsync(cancellationToken: cancellationToken))
        {
            if (!key.StartsWith("outbox:")) continue;

            try
            {
                var entry = await _kvContext.GetEntryAsync<byte[]>(key, cancellationToken: cancellationToken);
                if (entry?.Value != null)
                {
                    var message = JsonSerializer.Deserialize<OutboxMessage>(entry.Value);
                    if (message?.Status == OutboxStatus.Published && 
                        message.PublishedAt.HasValue && 
                        message.PublishedAt.Value < cutoff)
                    {
                        keysToDelete.Add(key);
                    }
                }
            }
            catch (NatsKVKeyNotFoundException)
            {
                // Already deleted
            }
        }

        foreach (var key in keysToDelete)
        {
            try
            {
                await _kvContext.DeleteAsync(key, cancellationToken: cancellationToken);
            }
            catch (NatsKVKeyNotFoundException)
            {
                // Already deleted
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _initLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
