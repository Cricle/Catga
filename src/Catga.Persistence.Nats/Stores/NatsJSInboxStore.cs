using Catga.Inbox;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using System.Text.Json;

namespace Catga.Persistence.Stores;

/// <summary>
/// NATS JetStream-based inbox store for idempotent message processing
/// </summary>
public sealed class NatsJSInboxStore : IInboxStore, IAsyncDisposable
{
    private readonly INatsConnection _connection;
    private readonly INatsJSContext _jetStream;
    private readonly string _streamName;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _initialized; // volatile 确保可见性

    public NatsJSInboxStore(INatsConnection connection, string? streamName = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _streamName = streamName ?? "CATGA_INBOX";
        _jetStream = new NatsJSContext(_connection);
    }

    /// <summary>
    /// Ensures the JetStream is initialized using double-checked locking pattern.
    /// Fast path (already initialized) has zero lock overhead.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        // Fast path: 已初始化则直接返回（零锁开销）
        if (_initialized) return;

        // Slow path: 需要初始化
        await InitializeSlowPathAsync(cancellationToken);
    }

    private async ValueTask InitializeSlowPathAsync(CancellationToken cancellationToken)
    {
        await _initLock.WaitAsync(cancellationToken);
        try
        {
            // 双重检查：防止多次初始化
            if (_initialized) return;

            var config = new StreamConfig(
                _streamName,
                new[] { $"{_streamName}.>" }
            )
            {
                Storage = StreamConfigStorage.File,
                Retention = StreamConfigRetention.Limits,
                MaxAge = TimeSpan.FromDays(7) // Keep processed messages for 7 days
            };

            try
            {
                await _jetStream.CreateStreamAsync(config, cancellationToken);
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 400)
            {
                // Stream already exists
            }

            // volatile write 确保初始化完成对其他线程可见
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

        var subject = $"{_streamName}.{messageId}";

        // Check if message already exists and is processed
        var existing = await GetMessageAsync(messageId, cancellationToken);
        if (existing != null)
        {
            if (existing.Status == InboxStatus.Processed)
                return false;

            if (existing.LockExpiresAt.HasValue && existing.LockExpiresAt.Value > DateTime.UtcNow)
                return false;
        }

        // Create or update with lock
        var message = existing ?? new InboxMessage
        {
            MessageId = messageId,
            MessageType = string.Empty,
            Payload = string.Empty
        };

        message.Status = InboxStatus.Processing;
        message.LockExpiresAt = DateTime.UtcNow.Add(lockDuration);

        var data = JsonSerializer.SerializeToUtf8Bytes(message);
        var ack = await _jetStream.PublishAsync(subject, data, cancellationToken: cancellationToken);

        return ack.Error == null;
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

        var subject = $"{_streamName}.{message.MessageId}";
        var data = JsonSerializer.SerializeToUtf8Bytes(message);

        await _jetStream.PublishAsync(subject, data, cancellationToken: cancellationToken);
    }

    public async ValueTask<bool> HasBeenProcessedAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        await EnsureInitializedAsync(cancellationToken);

        var message = await GetMessageAsync(messageId, cancellationToken);
        return message?.Status == InboxStatus.Processed;
    }

    public async ValueTask<string?> GetProcessedResultAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        await EnsureInitializedAsync(cancellationToken);

        var message = await GetMessageAsync(messageId, cancellationToken);
        return message?.Status == InboxStatus.Processed ? message.ProcessingResult : null;
    }

    public async ValueTask ReleaseLockAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        await EnsureInitializedAsync(cancellationToken);

        var message = await GetMessageAsync(messageId, cancellationToken);
        if (message != null)
        {
            message.Status = InboxStatus.Pending;
            message.LockExpiresAt = null;

            var subject = $"{_streamName}.{messageId}";
            var data = JsonSerializer.SerializeToUtf8Bytes(message);

            await _jetStream.PublishAsync(subject, data, cancellationToken: cancellationToken);
        }
    }

    public async ValueTask DeleteProcessedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default)
    {
        // JetStream with MaxAge handles this automatically
        await Task.CompletedTask;
    }

    private async Task<InboxMessage?> GetMessageAsync(string messageId, CancellationToken cancellationToken)
    {
        try
        {
            var subject = $"{_streamName}.{messageId}";
            var consumer = await _jetStream.CreateOrUpdateConsumerAsync(
                _streamName,
                new ConsumerConfig
                {
                    Name = $"inbox-get-{Guid.NewGuid():N}",
                    FilterSubject = subject,
                    AckPolicy = ConsumerConfigAckPolicy.None,
                    DeliverPolicy = ConsumerConfigDeliverPolicy.LastPerSubject
                },
                cancellationToken);

            await foreach (var msg in consumer.FetchAsync<byte[]>(
                new NatsJSFetchOpts { MaxMsgs = 1 },
                cancellationToken: cancellationToken))
            {
                if (msg.Data != null && msg.Data.Length > 0)
                {
                    return JsonSerializer.Deserialize<InboxMessage>(msg.Data);
                }
            }
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404)
        {
            // Stream or message doesn't exist
        }

        return null;
    }

    public ValueTask DisposeAsync()
    {
        _initLock.Dispose();
        return ValueTask.CompletedTask;
    }
}

