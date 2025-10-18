using Catga.Inbox;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Client.KeyValueStore;
using System.Text.Json;

namespace Catga.Persistence.Stores;

/// <summary>
/// NATS KV-based inbox store for idempotent message processing
/// </summary>
public sealed class NatsKVInboxStore : IInboxStore, IAsyncDisposable
{
    private readonly INatsConnection _connection;
    private readonly string _bucketName;
    private NatsKVContext? _kvContext;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public NatsKVInboxStore(INatsConnection connection, string? bucketName = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _bucketName = bucketName ?? "catga-inbox";
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

    public async ValueTask<bool> TryLockMessageAsync(
        string messageId,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        await EnsureInitializedAsync(cancellationToken);

        var key = $"inbox:{messageId}";

        try
        {
            var entry = await _kvContext!.GetEntryAsync<byte[]>(key, cancellationToken: cancellationToken);
            if (entry?.Value != null)
            {
                var existingMessage = JsonSerializer.Deserialize<InboxMessage>(entry.Value);
                if (existingMessage != null)
                {
                    // Already processed
                    if (existingMessage.Status == InboxStatus.Processed)
                        return false;

                    // Still locked
                    if (existingMessage.LockExpiresAt.HasValue && 
                        existingMessage.LockExpiresAt.Value > DateTime.UtcNow)
                        return false;

                    // Lock expired or pending, acquire lock
                    existingMessage.Status = InboxStatus.Processing;
                    existingMessage.LockExpiresAt = DateTime.UtcNow.Add(lockDuration);

                    var data = JsonSerializer.SerializeToUtf8Bytes(existingMessage);
                    await _kvContext.PutAsync(key, data, cancellationToken: cancellationToken);
                    return true;
                }
            }
        }
        catch (NatsKVKeyNotFoundException)
        {
            // Message doesn't exist, create new one
        }

        // Create new message with lock
        var newMessage = new InboxMessage
        {
            MessageId = messageId,
            MessageType = string.Empty,
            Payload = string.Empty,
            Status = InboxStatus.Processing,
            LockExpiresAt = DateTime.UtcNow.Add(lockDuration)
        };

        var newData = JsonSerializer.SerializeToUtf8Bytes(newMessage);
        await _kvContext!.PutAsync(key, newData, cancellationToken: cancellationToken);
        return true;
    }

    public async ValueTask MarkAsProcessedAsync(
        InboxMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await EnsureInitializedAsync(cancellationToken);

        message.ProcessedAt = DateTime.UtcNow;
        message.Status = InboxStatus.Processed;
        message.LockExpiresAt = null;

        var key = $"inbox:{message.MessageId}";
        var data = JsonSerializer.SerializeToUtf8Bytes(message);

        await _kvContext!.PutAsync(key, data, cancellationToken: cancellationToken);
    }

    public async ValueTask<bool> HasBeenProcessedAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        await EnsureInitializedAsync(cancellationToken);

        var key = $"inbox:{messageId}";

        try
        {
            var entry = await _kvContext!.GetEntryAsync<byte[]>(key, cancellationToken: cancellationToken);
            if (entry?.Value != null)
            {
                var message = JsonSerializer.Deserialize<InboxMessage>(entry.Value);
                return message?.Status == InboxStatus.Processed;
            }
        }
        catch (NatsKVKeyNotFoundException)
        {
            // Message doesn't exist
        }

        return false;
    }

    public async ValueTask<string?> GetProcessedResultAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        await EnsureInitializedAsync(cancellationToken);

        var key = $"inbox:{messageId}";

        try
        {
            var entry = await _kvContext!.GetEntryAsync<byte[]>(key, cancellationToken: cancellationToken);
            if (entry?.Value != null)
            {
                var message = JsonSerializer.Deserialize<InboxMessage>(entry.Value);
                if (message?.Status == InboxStatus.Processed)
                {
                    return message.ProcessingResult;
                }
            }
        }
        catch (NatsKVKeyNotFoundException)
        {
            // Message doesn't exist
        }

        return null;
    }

    public async ValueTask ReleaseLockAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        await EnsureInitializedAsync(cancellationToken);

        var key = $"inbox:{messageId}";

        try
        {
            var entry = await _kvContext!.GetEntryAsync<byte[]>(key, cancellationToken: cancellationToken);
            if (entry?.Value != null)
            {
                var message = JsonSerializer.Deserialize<InboxMessage>(entry.Value);
                if (message != null)
                {
                    message.Status = InboxStatus.Pending;
                    message.LockExpiresAt = null;

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

    public async ValueTask DeleteProcessedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var cutoff = DateTime.UtcNow - retentionPeriod;
        var keysToDelete = new List<string>();

        await foreach (var key in _kvContext!.GetKeysAsync(cancellationToken: cancellationToken))
        {
            if (!key.StartsWith("inbox:")) continue;

            try
            {
                var entry = await _kvContext.GetEntryAsync<byte[]>(key, cancellationToken: cancellationToken);
                if (entry?.Value != null)
                {
                    var message = JsonSerializer.Deserialize<InboxMessage>(entry.Value);
                    if (message?.Status == InboxStatus.Processed && 
                        message.ProcessedAt.HasValue && 
                        message.ProcessedAt.Value < cutoff)
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
