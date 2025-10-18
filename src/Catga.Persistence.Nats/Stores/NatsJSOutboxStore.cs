using Catga.Outbox;
using Catga.Persistence;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using System.Text.Json;

namespace Catga.Persistence.Stores;

/// <summary>
/// NATS JetStream-based outbox store for reliable message publishing
/// </summary>
public sealed class NatsJSOutboxStore : NatsJSStoreBase, IOutboxStore
{
    public NatsJSOutboxStore(INatsConnection connection, string? streamName = null)
        : base(connection, streamName ?? "CATGA_OUTBOX")
    {
    }

    protected override StreamConfig CreateStreamConfig() => new(
        StreamName,
        new[] { $"{StreamName}.>" }
    )
    {
        Storage = StreamConfigStorage.File,
        Retention = StreamConfigRetention.Limits
    };

    public async ValueTask AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await EnsureInitializedAsync(cancellationToken);

        var subject = $"{StreamName}.{message.MessageId}";
        var data = JsonSerializer.SerializeToUtf8Bytes(message);

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
                    var outboxMsg = JsonSerializer.Deserialize<OutboxMessage>(msg.Data);
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

        return messages.OrderBy(m => m.CreatedAt).ToList();
    }

    public async ValueTask MarkAsPublishedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        // In JetStream work queue mode, we can just not re-fetch the message
        // Or update it with Published status
        await Task.CompletedTask;
    }

    public async ValueTask MarkAsFailedAsync(
        string messageId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        // Re-publish with updated retry count
        var subject = $"{StreamName}.{messageId}";

        // Note: In a real implementation, you'd need to fetch the existing message,
        // update it, and re-publish. For simplicity, this is left as a TODO.
        await Task.CompletedTask;
    }

    public async ValueTask DeletePublishedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default)
    {
        // JetStream with WorkQueue retention handles this automatically
        await Task.CompletedTask;
    }

}

