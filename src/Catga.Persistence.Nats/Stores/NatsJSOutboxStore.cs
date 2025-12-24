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
                    new ConsumerConfig { Name = $"outbox-reader-{Guid.NewGuid():N}", AckPolicy = ConsumerConfigAckPolicy.None, DeliverPolicy = ConsumerConfigDeliverPolicy.All }, ct);

                await foreach (var msg in consumer.FetchAsync<byte[]>(new NatsJSFetchOpts { MaxMsgs = maxCount * 2 }, cancellationToken: ct))
                {
                    if (msg.Data is { Length: > 0 })
                    {
                        var outboxMsg = (OutboxMessage?)serializer.Deserialize(msg.Data, typeof(OutboxMessage));
                        if (outboxMsg is { Status: OutboxStatus.Pending } && outboxMsg.RetryCount < outboxMsg.MaxRetries)
                        {
                            messages.Add(outboxMsg);
                            if (messages.Count >= maxCount) break;
                        }
                    }
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

            try
            {
                var consumer = await JetStream.CreateOrUpdateConsumerAsync(StreamName,
                    new ConsumerConfig { Name = $"outbox-publisher-{Guid.NewGuid():N}", AckPolicy = ConsumerConfigAckPolicy.Explicit, FilterSubjects = [subject] }, ct);

                await foreach (var msg in consumer.FetchAsync<byte[]>(new NatsJSFetchOpts { MaxMsgs = 1 }, cancellationToken: ct))
                {
                    if (msg.Data is { Length: > 0 })
                    {
                        var outboxMsg = (OutboxMessage?)serializer.Deserialize(msg.Data, typeof(OutboxMessage));
                        if (outboxMsg != null && outboxMsg.MessageId == messageId)
                        {
                            outboxMsg.Status = OutboxStatus.Published;
                            outboxMsg.PublishedAt = DateTime.UtcNow;
                            var updatedData = serializer.Serialize(outboxMsg);
                            var ack = await JetStream.PublishAsync(subject, updatedData, cancellationToken: ct);
                            if (ack.Error != null) throw new InvalidOperationException($"Failed to mark outbox message as published: {ack.Error.Description}");
                            await msg.AckAsync(cancellationToken: ct);
                            CatgaDiagnostics.OutboxPublished.Add(1);
                            break;
                        }
                    }
                }
                await JetStream.DeleteConsumerAsync(StreamName, consumer.Info.Name, ct);
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404) { }
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

            try
            {
                var consumer = await JetStream.CreateOrUpdateConsumerAsync(StreamName,
                    new ConsumerConfig { Name = $"outbox-updater-{Guid.NewGuid():N}", AckPolicy = ConsumerConfigAckPolicy.Explicit, FilterSubjects = [subject] }, ct);

                await foreach (var msg in consumer.FetchAsync<byte[]>(new NatsJSFetchOpts { MaxMsgs = 1 }, cancellationToken: ct))
                {
                    if (msg.Data is { Length: > 0 })
                    {
                        var outboxMsg = (OutboxMessage?)serializer.Deserialize(msg.Data, typeof(OutboxMessage));
                        if (outboxMsg != null && outboxMsg.MessageId == messageId)
                        {
                            outboxMsg.RetryCount++;
                            outboxMsg.LastError = errorMessage;
                            outboxMsg.Status = outboxMsg.RetryCount >= outboxMsg.MaxRetries ? OutboxStatus.Failed : OutboxStatus.Pending;
                            var updatedData = serializer.Serialize(outboxMsg);
                            var ack = await JetStream.PublishAsync(subject, updatedData, cancellationToken: ct);
                            if (ack.Error != null) throw new InvalidOperationException($"Failed to update outbox message: {ack.Error.Description}");
                            await msg.AckAsync(cancellationToken: ct);
                            CatgaDiagnostics.OutboxFailed.Add(1);
                            break;
                        }
                    }
                }
                await JetStream.DeleteConsumerAsync(StreamName, consumer.Info.Name, ct);
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404) { }
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
                await foreach (var msg in consumer.FetchAsync<byte[]>(new NatsJSFetchOpts { MaxMsgs = 1000 }, cancellationToken: ct))
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
}
