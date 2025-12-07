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

    protected override string[] GetSubjects() => new[] { $"{StreamName}.>" };

    public async ValueTask AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Outbox.Add", ActivityKind.Producer);
            ArgumentNullException.ThrowIfNull(message);

            await EnsureInitializedAsync(ct);

            var subject = $"{StreamName}.{message.MessageId}";
            var data = serializer.Serialize(message, typeof(OutboxMessage));
            try
            {
                var ack = await JetStream.PublishAsync(subject, data, cancellationToken: ct);
                if (ack.Error != null)
                {
                    throw new InvalidOperationException($"Failed to add outbox message: {ack.Error.Description}");
                }
                activity?.AddActivityEvent(CatgaActivitySource.Events.OutboxAdded,
                    ("message.id", message.MessageId),
                    ("bytes", data.Length));
                CatgaDiagnostics.OutboxAdded.Add(1);
            }
            catch (Exception ex)
            {
                activity?.SetError(ex);
                throw;
            }
        }, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default)
    {
        return await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Outbox.GetPending", ActivityKind.Internal);
            await EnsureInitializedAsync(ct);

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
                    ct);

                await foreach (var msg in consumer.FetchAsync<byte[]>(
                    new NatsJSFetchOpts { MaxMsgs = maxCount * 2 },
                    cancellationToken: ct))
                {
                    if (msg.Data != null && msg.Data.Length > 0)
                    {
                        var outboxMsg = (OutboxMessage?)serializer.Deserialize(msg.Data, typeof(OutboxMessage));
                        if (outboxMsg != null &&
                            outboxMsg.Status == OutboxStatus.Pending &&
                            outboxMsg.RetryCount < outboxMsg.MaxRetries)
                        {
                            messages.Add(outboxMsg);
                            activity?.AddActivityEvent(CatgaActivitySource.Events.OutboxGetPendingItem,
                                ("message.id", outboxMsg.MessageId));
                            if (messages.Count >= maxCount) break;
                        }
                    }
                }
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404)
            {
                // Stream doesn't exist yet
                activity?.AddActivityEvent(CatgaActivitySource.Events.OutboxGetPendingNotFound);
            }

            if (messages.Count > 1)
            {
                messages.Sort((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));
            }
            activity?.AddActivityEvent(CatgaActivitySource.Events.OutboxGetPendingDone,
                ("count", messages.Count));
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
                // 1. Fetch the existing message
                var consumer = await JetStream.CreateOrUpdateConsumerAsync(
                    StreamName,
                    new ConsumerConfig
                    {
                        Name = $"outbox-publisher-{Guid.NewGuid():N}",
                        AckPolicy = ConsumerConfigAckPolicy.Explicit,
                        FilterSubjects = new[] { subject }
                    },
                    ct);

                await foreach (var msg in consumer.FetchAsync<byte[]>(
                    new NatsJSFetchOpts { MaxMsgs = 1 },
                    cancellationToken: ct))
                {
                    if (msg.Data != null && msg.Data.Length > 0)
                    {
                        // 2. Deserialize existing message
                        var outboxMsg = (OutboxMessage?)serializer.Deserialize(msg.Data, typeof(OutboxMessage));
                        if (outboxMsg != null && outboxMsg.MessageId == messageId)
                        {
                            // 3. Update status and timestamp
                            outboxMsg.Status = OutboxStatus.Published;
                            outboxMsg.PublishedAt = DateTime.UtcNow;

                            // 4. Re-publish with updated data
                            var updatedData = serializer.Serialize(outboxMsg, typeof(OutboxMessage));
                            var ack = await JetStream.PublishAsync(subject, updatedData, cancellationToken: ct);

                            if (ack.Error != null)
                            {
                                throw new InvalidOperationException($"Failed to mark outbox message as published: {ack.Error.Description}");
                            }

                            // 5. Acknowledge the old message
                            await msg.AckAsync(cancellationToken: ct);
                            CatgaDiagnostics.OutboxPublished.Add(1);
                            activity?.AddActivityEvent(CatgaActivitySource.Events.OutboxMarkPublished,
                                ("message.id", messageId));
                            break;
                        }
                    }
                }

                // Clean up temporary consumer
                await JetStream.DeleteConsumerAsync(StreamName, consumer.Info.Name, ct);
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404)
            {
                // Message not found - it may have been already processed or deleted
                // This is not an error condition for idempotency
                activity?.AddActivityEvent(CatgaActivitySource.Events.OutboxMarkPublishedNotFound,
                    ("message.id", messageId));
            }
        }, cancellationToken);
    }

    public async ValueTask MarkAsFailedAsync(
        long messageId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Outbox.MarkFailed", ActivityKind.Internal);
            ArgumentNullException.ThrowIfNull(errorMessage);

            await EnsureInitializedAsync(ct);

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
                    ct);

                await foreach (var msg in consumer.FetchAsync<byte[]>(
                    new NatsJSFetchOpts { MaxMsgs = 1 },
                    cancellationToken: ct))
                {
                    if (msg.Data != null && msg.Data.Length > 0)
                    {
                        // 2. Deserialize existing message
                        var outboxMsg = (OutboxMessage?)serializer.Deserialize(msg.Data, typeof(OutboxMessage));
                        if (outboxMsg != null && outboxMsg.MessageId == messageId)
                        {
                            // 3. Update fields
                            outboxMsg.RetryCount++;
                            outboxMsg.LastError = errorMessage;
                            outboxMsg.Status = outboxMsg.RetryCount >= outboxMsg.MaxRetries
                                ? OutboxStatus.Failed
                                : OutboxStatus.Pending;

                            // 4. Re-publish with updated data
                            var updatedData = serializer.Serialize(outboxMsg, typeof(OutboxMessage));
                            var ack = await JetStream.PublishAsync(subject, updatedData, cancellationToken: ct);

                            if (ack.Error != null)
                            {
                                throw new InvalidOperationException($"Failed to update outbox message: {ack.Error.Description}");
                            }

                            // 5. Acknowledge the old message (removes it from stream with Limits retention)
                            await msg.AckAsync(cancellationToken: ct);
                            CatgaDiagnostics.OutboxFailed.Add(1);
                            activity?.AddActivityEvent(CatgaActivitySource.Events.OutboxMarkFailedUpdated,
                                ("message.id", messageId),
                                ("retry", outboxMsg.RetryCount),
                                ("status", (int)outboxMsg.Status));
                            break;
                        }
                    }
                }

                // Clean up temporary consumer
                await JetStream.DeleteConsumerAsync(StreamName, consumer.Info.Name, ct);
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404)
            {
                // Message not found - it may have been already processed or deleted
                // This is not an error condition for idempotency
                activity?.AddActivityEvent(CatgaActivitySource.Events.OutboxMarkFailedNotFound,
                    ("message.id", messageId));
            }
        }, cancellationToken);
    }

    public async ValueTask DeletePublishedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default)
    {
        await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Outbox.DeletePublished", ActivityKind.Internal);
            await EnsureInitializedAsync(ct);

            var cutoffTime = DateTime.UtcNow.Subtract(retentionPeriod);
            var deletedCount = 0;

            try
            {
                // Create a temporary consumer to scan all messages
                var consumer = await JetStream.CreateOrUpdateConsumerAsync(
                    StreamName,
                    new ConsumerConfig
                    {
                        Name = $"outbox-cleaner-{Guid.NewGuid():N}",
                        AckPolicy = ConsumerConfigAckPolicy.None,
                        DeliverPolicy = ConsumerConfigDeliverPolicy.All
                    },
                    ct);

                var toDelete = new List<ulong>();

                await foreach (var msg in consumer.FetchAsync<byte[]>(
                    new NatsJSFetchOpts { MaxMsgs = 1000 },
                    cancellationToken: ct))
                {
                    if (msg.Data != null && msg.Data.Length > 0)
                    {
                        var outboxMsg = (OutboxMessage?)serializer.Deserialize(msg.Data, typeof(OutboxMessage));
                        if (outboxMsg != null &&
                            outboxMsg.Status == OutboxStatus.Published &&
                            outboxMsg.PublishedAt.HasValue &&
                            outboxMsg.PublishedAt.Value < cutoffTime)
                        {
                            // Mark for deletion by sequence
                            if (msg.Metadata?.Sequence.Stream > 0)
                            {
                                toDelete.Add(msg.Metadata.Value.Sequence.Stream);
                            }
                        }
                    }
                }

                // Delete messages by sequence
                foreach (var seq in toDelete)
                {
                    try
                    {
                        await JetStream.DeleteMessageAsync(StreamName, new StreamMsgDeleteRequest { Seq = seq }, ct);
                        deletedCount++;
                    }
                    catch (NatsJSApiException) { /* ignore individual failures */ }
                }

                // Clean up temporary consumer
                try
                {
                    await JetStream.DeleteConsumerAsync(StreamName, consumer.Info.Name, ct);
                }
                catch { /* ignore */ }

                activity?.AddEvent(new System.Diagnostics.ActivityEvent("outbox.delete_published.done",
                    tags: new System.Diagnostics.ActivityTagsCollection
                    {
                        { "deleted", deletedCount },
                        { "retention_hours", retentionPeriod.TotalHours }
                    }));
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404)
            {
                // Stream doesn't exist
                activity?.AddEvent(new System.Diagnostics.ActivityEvent("outbox.delete_published.not_found"));
            }
        }, cancellationToken);
    }

}

