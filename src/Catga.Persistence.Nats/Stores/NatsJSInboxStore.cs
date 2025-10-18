using Catga.Inbox;
using Catga.Persistence;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using System.Text.Json;

namespace Catga.Persistence.Stores;

/// <summary>
/// NATS JetStream-based inbox store for idempotent message processing
/// </summary>
public sealed class NatsJSInboxStore : NatsJSStoreBase, IInboxStore
{
    public NatsJSInboxStore(INatsConnection connection, string? streamName = null)
        : base(connection, streamName ?? "CATGA_INBOX")
    {
    }

    protected override StreamConfig CreateStreamConfig() => new(
        StreamName,
        new[] { $"{StreamName}.>" }
    )
    {
        Storage = StreamConfigStorage.File,
        Retention = StreamConfigRetention.Limits,
        MaxAge = TimeSpan.FromDays(7) // Keep processed messages for 7 days
    };

    public async ValueTask<bool> TryLockMessageAsync(
        string messageId,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        await EnsureInitializedAsync(cancellationToken);

        var subject = $"{StreamName}.{messageId}";

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
        var ack = await JetStream.PublishAsync(subject, data, cancellationToken: cancellationToken);

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

        var subject = $"{StreamName}.{message.MessageId}";
        var data = JsonSerializer.SerializeToUtf8Bytes(message);

        await JetStream.PublishAsync(subject, data, cancellationToken: cancellationToken);
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

            var subject = $"{StreamName}.{messageId}";
            var data = JsonSerializer.SerializeToUtf8Bytes(message);

            await JetStream.PublishAsync(subject, data, cancellationToken: cancellationToken);
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
            var subject = $"{StreamName}.{messageId}";
            var consumer = await JetStream.CreateOrUpdateConsumerAsync(
                StreamName,
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

}

