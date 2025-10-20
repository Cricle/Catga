using Catga.Abstractions;
using Catga.Outbox;
using Catga.Persistence;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Catga.Persistence.Stores;

/// <summary>
/// NATS JetStream-based outbox store for reliable message publishing
/// </summary>
public sealed class NatsJSOutboxStore : NatsJSStoreBase, IOutboxStore
{
    private readonly IMessageSerializer _serializer;

    public NatsJSOutboxStore(
        INatsConnection connection,
        IMessageSerializer serializer,
        string? streamName = null,
        NatsJSStoreOptions? options = null)
        : base(connection, streamName ?? "CATGA_OUTBOX", options)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    protected override string[] GetSubjects() => new[] { $"{StreamName}.>" };

    public async ValueTask AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await EnsureInitializedAsync(cancellationToken);

        var subject = $"{StreamName}.{message.MessageId}";
        var data = _serializer.Serialize(message);

        var ack = await JetStream.PublishAsync(subject, data, cancellationToken: cancellationToken);

        if (ack.Error != null)
        {
            throw new InvalidOperationException($"Failed to add outbox message: {ack.Error.Description}");
        }
    }

    public async ValueTask<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var messages = new List<OutboxMessage>();

        try
        {
            var consumer = await JetStream.CreateOrUpdateConsumerAsync(
                StreamName,
                new ConsumerConfig
                {
                    Name = $"outbox-reader-{Guid.NewGuid():N}",
                    AckPolicy = ConsumerConfigAckPolicy.None,
                    DeliverPolicy = ConsumerConfigDeliverPolicy.All
                },
                cancellationToken);

            await foreach (var msg in consumer.FetchAsync<byte[]>(
                new NatsJSFetchOpts { MaxMsgs = maxCount * 2 }, // Fetch more to filter
                cancellationToken: cancellationToken))
            {
                if (msg.Data != null && msg.Data.Length > 0)
                {
                    var outboxMsg = _serializer.Deserialize<OutboxMessage>(msg.Data);
                    if (outboxMsg != null &&
                        outboxMsg.Status == OutboxStatus.Pending &&
                        outboxMsg.RetryCount < outboxMsg.MaxRetries)
                    {
                        messages.Add(outboxMsg);
                        if (messages.Count >= maxCount) break;
                    }
                }
            }
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404)
        {
            // Stream doesn't exist yet
        }

        // Manual sort instead of LINQ OrderBy
        if (messages.Count > 1)
        {
            messages.Sort((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));
        }
        
        return messages;
    }

    public async ValueTask MarkAsPublishedAsync(long messageId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageId);

        await EnsureInitializedAsync(cancellationToken);

        var subject = $"{StreamName}.{messageId}";

        try
        {
            // 1. Fetch the existing message
            var consumer = await JetStream.CreateOrUpdateConsumerAsync(
                StreamName,
                new ConsumerConfig
                {
                    Name = $"outbox-publisher-{Guid.NewGuid():N}",
                    AckPolicy = ConsumerConfigAckPolicy.Explicit,
                    FilterSubjects = new[] { subject }
                },
                cancellationToken);

            await foreach (var msg in consumer.FetchAsync<byte[]>(
                new NatsJSFetchOpts { MaxMsgs = 1 },
                cancellationToken: cancellationToken))
            {
                if (msg.Data != null && msg.Data.Length > 0)
                {
                    // 2. Deserialize existing message
                    var outboxMsg = _serializer.Deserialize<OutboxMessage>(msg.Data);
                    if (outboxMsg != null && outboxMsg.MessageId == messageId)
                    {
                        // 3. Update status and timestamp
                        outboxMsg.Status = OutboxStatus.Published;
                        outboxMsg.PublishedAt = DateTime.UtcNow;

                        // 4. Re-publish with updated data
                        var updatedData = _serializer.Serialize(outboxMsg);
                        var ack = await JetStream.PublishAsync(subject, updatedData, cancellationToken: cancellationToken);

                        if (ack.Error != null)
                        {
                            throw new InvalidOperationException($"Failed to mark outbox message as published: {ack.Error.Description}");
                        }

                        // 5. Acknowledge the old message
                        await msg.AckAsync(cancellationToken: cancellationToken);
                        break;
                    }
                }
            }

            // Clean up temporary consumer
            await JetStream.DeleteConsumerAsync(StreamName, consumer.Info.Name, cancellationToken);
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404)
        {
            // Message not found - it may have been already processed or deleted
            // This is not an error condition for idempotency
        }
    }

    public async ValueTask MarkAsFailedAsync(
        long messageId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageId);
        ArgumentNullException.ThrowIfNull(errorMessage);

        await EnsureInitializedAsync(cancellationToken);

        var subject = $"{StreamName}.{messageId}";

        try
        {
            // 1. Fetch the existing message
            var consumer = await JetStream.CreateOrUpdateConsumerAsync(
                StreamName,
                new ConsumerConfig
                {
                    Name = $"outbox-updater-{Guid.NewGuid():N}",
                    AckPolicy = ConsumerConfigAckPolicy.Explicit,
                    FilterSubjects = new[] { subject }
                },
                cancellationToken);

            await foreach (var msg in consumer.FetchAsync<byte[]>(
                new NatsJSFetchOpts { MaxMsgs = 1 },
                cancellationToken: cancellationToken))
            {
                if (msg.Data != null && msg.Data.Length > 0)
                {
                    // 2. Deserialize existing message
                    var outboxMsg = _serializer.Deserialize<OutboxMessage>(msg.Data);
                    if (outboxMsg != null && outboxMsg.MessageId == messageId)
                    {
                        // 3. Update fields
                        outboxMsg.RetryCount++;
                        outboxMsg.LastError = errorMessage;
                        outboxMsg.Status = outboxMsg.RetryCount >= outboxMsg.MaxRetries
                            ? OutboxStatus.Failed
                            : OutboxStatus.Pending;

                        // 4. Re-publish with updated data
                        var updatedData = _serializer.Serialize(outboxMsg);
                        var ack = await JetStream.PublishAsync(subject, updatedData, cancellationToken: cancellationToken);

                        if (ack.Error != null)
                        {
                            throw new InvalidOperationException($"Failed to update outbox message: {ack.Error.Description}");
                        }

                        // 5. Acknowledge the old message (removes it from stream with Limits retention)
                        await msg.AckAsync(cancellationToken: cancellationToken);
                        break;
                    }
                }
            }

            // Clean up temporary consumer
            await JetStream.DeleteConsumerAsync(StreamName, consumer.Info.Name, cancellationToken);
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404)
        {
            // Message not found - it may have been already processed or deleted
            // This is not an error condition for idempotency
        }
    }

    public async ValueTask DeletePublishedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default)
    {
        // JetStream with WorkQueue retention handles this automatically
        await Task.CompletedTask;
    }

}

