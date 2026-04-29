using System.Diagnostics;
using Catga.Abstractions;
using Catga.Observability;
using Catga.Outbox;
using Catga.Resilience;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Catga.Persistence.Stores;

/// <summary>NATS JetStream-based outbox store.</summary>
public sealed class NatsJSOutboxStore(INatsConnection connection, IMessageSerializer serializer, IResiliencePipelineProvider provider, string? streamName = null, NatsJSStoreOptions? options = null)
    : NatsJSStoreBase(connection, streamName ?? "CATGA_OUTBOX", options), IOutboxStore
{
    protected override string[] GetSubjects() => [$"{StreamName}.>"];

    protected override StreamConfig CreateStreamConfig()
    {
        var config = base.CreateStreamConfig();
        config.MaxMsgsPerSubject = 1;
        return config;
    }

    public async ValueTask AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Outbox.Add", ActivityKind.Producer);
            ArgumentNullException.ThrowIfNull(message);
            await EnsureInitializedAsync(ct);

            var subject = $"{StreamName}.{message.MessageId}";
            var data = serializer.Serialize(message);
            var ack = await JetStream.PublishAsync(subject, data, cancellationToken: ct);
            if (ack.Error != null) throw new InvalidOperationException($"Failed to add outbox message: {ack.Error.Description}");
            CatgaDiagnostics.OutboxAdded.Add(1);
        }, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int maxCount = 100, CancellationToken cancellationToken = default)
    {
        return await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Outbox.GetPending", ActivityKind.Internal);
            await EnsureInitializedAsync(ct);
            var messages = new List<OutboxMessage>();

            try
            {
                var consumer = await JetStream.CreateOrUpdateConsumerAsync(StreamName,
                    new ConsumerConfig
                    {
                        Name = $"outbox-reader-{Guid.NewGuid():N}",
                        FilterSubject = $"{StreamName}.>",
                        AckPolicy = ConsumerConfigAckPolicy.None,
                        DeliverPolicy = ConsumerConfigDeliverPolicy.LastPerSubject
                    },
                    ct);

                try
                {
                    await foreach (var msg in consumer.FetchNoWaitAsync<byte[]>(
                        new NatsJSFetchOpts { MaxMsgs = Math.Max(maxCount * 10, 1000) },
                        cancellationToken: ct))
                    {
                        if (msg.Data is not { Length: > 0 })
                        {
                            continue;
                        }

                        var outboxMsg = (OutboxMessage?)serializer.Deserialize(msg.Data, typeof(OutboxMessage));
                        if (outboxMsg is { Status: OutboxStatus.Pending } && outboxMsg.RetryCount < outboxMsg.MaxRetries)
                        {
                            messages.Add(outboxMsg);
                            if (messages.Count >= maxCount) break;
                        }
                    }
                }
                finally
                {
                    try { await JetStream.DeleteConsumerAsync(StreamName, consumer.Info.Name, ct); } catch { }
                }
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404) { }

            if (messages.Count > 1) messages.Sort((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));
            return (IReadOnlyList<OutboxMessage>)messages;
        }, cancellationToken);
    }

    public async ValueTask MarkAsPublishedAsync(long messageId, CancellationToken cancellationToken = default)
    {
        await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Outbox.MarkPublished", ActivityKind.Internal);
            await EnsureInitializedAsync(ct);
            var subject = $"{StreamName}.{messageId}";
            var current = await GetLatestMessageAsync(messageId, ct);
            if (current is null)
            {
                return;
            }

            var latest = current.Value;
            latest.Message.Status = OutboxStatus.Published;
            latest.Message.PublishedAt = DateTime.UtcNow;
            var updatedData = serializer.Serialize(latest.Message);
            var ack = await JetStream.PublishAsync(
                subject,
                updatedData,
                opts: new NatsJSPubOpts { ExpectedLastSubjectSequence = latest.Sequence },
                cancellationToken: ct);
            if (ack.Error != null) throw new InvalidOperationException($"Failed to mark outbox message as published: {ack.Error.Description}");
            CatgaDiagnostics.OutboxPublished.Add(1);
        }, cancellationToken);
    }

    public async ValueTask MarkAsFailedAsync(long messageId, string errorMessage, CancellationToken cancellationToken = default)
    {
        await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Outbox.MarkFailed", ActivityKind.Internal);
            ArgumentNullException.ThrowIfNull(errorMessage);
            await EnsureInitializedAsync(ct);
            var subject = $"{StreamName}.{messageId}";
            var current = await GetLatestMessageAsync(messageId, ct);
            if (current is null)
            {
                return;
            }

            var latest = current.Value;
            latest.Message.RetryCount++;
            latest.Message.LastError = errorMessage;
            latest.Message.Status = latest.Message.RetryCount >= latest.Message.MaxRetries ? OutboxStatus.Failed : OutboxStatus.Pending;
            var updatedData = serializer.Serialize(latest.Message);
            var ack = await JetStream.PublishAsync(
                subject,
                updatedData,
                opts: new NatsJSPubOpts { ExpectedLastSubjectSequence = latest.Sequence },
                cancellationToken: ct);
            if (ack.Error != null) throw new InvalidOperationException($"Failed to update outbox message: {ack.Error.Description}");
            CatgaDiagnostics.OutboxFailed.Add(1);
        }, cancellationToken);
    }

    public async ValueTask DeletePublishedMessagesAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default)
    {
        await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Outbox.DeletePublished", ActivityKind.Internal);
            await EnsureInitializedAsync(ct);
            var cutoffTime = DateTime.UtcNow.Subtract(retentionPeriod);

            try
            {
                var consumer = await JetStream.CreateOrUpdateConsumerAsync(StreamName,
                    new ConsumerConfig { Name = $"outbox-cleaner-{Guid.NewGuid():N}", AckPolicy = ConsumerConfigAckPolicy.None, DeliverPolicy = ConsumerConfigDeliverPolicy.All }, ct);

                var toDelete = new List<ulong>();
                await foreach (var msg in consumer.FetchNoWaitAsync<byte[]>(
                    new NatsJSFetchOpts { MaxMsgs = 1000 },
                    cancellationToken: ct))
                {
                    if (msg.Data is { Length: > 0 })
                    {
                        var outboxMsg = (OutboxMessage?)serializer.Deserialize(msg.Data, typeof(OutboxMessage));
                        if (outboxMsg is { Status: OutboxStatus.Published, PublishedAt: not null } && outboxMsg.PublishedAt.Value < cutoffTime)
                            if (msg.Metadata?.Sequence.Stream > 0) toDelete.Add(msg.Metadata.Value.Sequence.Stream);
                    }
                }

                foreach (var seq in toDelete)
                    try { await JetStream.DeleteMessageAsync(StreamName, new StreamMsgDeleteRequest { Seq = seq }, ct); } catch { }

                try { await JetStream.DeleteConsumerAsync(StreamName, consumer.Info.Name, ct); } catch { }
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404) { }
        }, cancellationToken);
    }

    private async Task<(OutboxMessage Message, ulong Sequence)?> GetLatestMessageAsync(long messageId, CancellationToken cancellationToken)
    {
        var subject = $"{StreamName}.{messageId}";

        try
        {
            var consumer = await JetStream.CreateOrUpdateConsumerAsync(
                StreamName,
                new ConsumerConfig
                {
                    Name = $"outbox-get-{Guid.NewGuid():N}",
                    FilterSubject = subject,
                    AckPolicy = ConsumerConfigAckPolicy.None,
                    DeliverPolicy = ConsumerConfigDeliverPolicy.LastPerSubject
                },
                cancellationToken);

            try
            {
                await foreach (var msg in consumer.FetchNoWaitAsync<byte[]>(
                    new NatsJSFetchOpts { MaxMsgs = 1 },
                    cancellationToken: cancellationToken))
                {
                    if (msg.Data is not { Length: > 0 })
                    {
                        continue;
                    }

                    var outboxMsg = (OutboxMessage?)serializer.Deserialize(msg.Data, typeof(OutboxMessage));
                    if (outboxMsg != null && msg.Metadata is { Sequence.Stream: > 0 } metadata)
                    {
                        return (outboxMsg, metadata.Sequence.Stream);
                    }
                }
            }
            finally
            {
                try { await JetStream.DeleteConsumerAsync(StreamName, consumer.Info.Name, cancellationToken); } catch { }
            }
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404) { }

        return null;
    }
}
