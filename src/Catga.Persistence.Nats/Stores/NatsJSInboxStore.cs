using Catga.Inbox;
using Catga.Serialization;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Catga.Persistence.Stores;

/// <summary>
/// NATS JetStream-based inbox store for idempotent message processing
/// </summary>
public sealed class NatsJSInboxStore : IInboxStore, IAsyncDisposable
{
    private readonly INatsConnection _connection;
    private readonly INatsJSContext _jetStream;
    private readonly IMessageSerializer _serializer;
    private readonly string _streamName;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public NatsJSInboxStore(
        INatsConnection connection,
        IMessageSerializer serializer,
        string? streamName = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _streamName = streamName ?? "CATGA_INBOX";
        _jetStream = new NatsJSContext(_connection);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
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

        var data = _serializer.Serialize(message);
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
        var data = _serializer.Serialize(message);

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
            var data = _serializer.Serialize(message);

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
                    return _serializer.Deserialize<InboxMessage>(msg.Data);
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

