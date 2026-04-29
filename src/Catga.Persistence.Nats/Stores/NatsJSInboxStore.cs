using System.Diagnostics;
using Catga.Abstractions;
using Catga.Inbox;
using Catga.Observability;
using Catga.Resilience;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Catga.Persistence.Stores;

/// <summary>NATS JetStream-based inbox store.</summary>
public sealed class NatsJSInboxStore(INatsConnection connection, IMessageSerializer serializer, IResiliencePipelineProvider provider, string? streamName = null, NatsJSStoreOptions? options = null)
    : NatsJSStoreBase(connection, streamName ?? "CATGA_INBOX", options), IInboxStore
{
    protected override string[] GetSubjects() => [$"{StreamName}.>"];

    protected override StreamConfig CreateStreamConfig()
    {
        var config = base.CreateStreamConfig();
        config.MaxMsgsPerSubject = 1;
        return config;
    }

    public async ValueTask<bool> TryLockMessageAsync(long messageId, TimeSpan lockDuration, CancellationToken cancellationToken = default)
    {
        // No retry for lock operations - they are not idempotent
        return await provider.ExecutePersistenceNoRetryAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Inbox.TryLock", ActivityKind.Internal);
            await EnsureInitializedAsync(ct);
            var subject = $"{StreamName}.{messageId}";

            var current = await GetMessageStateAsync(messageId, ct);
            var existing = current?.Message;
            if (existing != null)
            {
                if (existing.Status == InboxStatus.Processed) return false;
                if (existing.LockExpiresAt.HasValue && existing.LockExpiresAt.Value > DateTime.UtcNow) return false;
            }

            var message = existing ?? new InboxMessage { MessageId = messageId, MessageType = string.Empty, Payload = [] };
            message.Status = InboxStatus.Processing;
            message.LockExpiresAt = DateTime.UtcNow.Add(lockDuration);

            var data = serializer.Serialize(message);
            var ack = await JetStream.PublishAsync(
                subject,
                data,
                opts: new NatsJSPubOpts { ExpectedLastSubjectSequence = current?.Sequence ?? 0 },
                cancellationToken: ct);
            if (ack.Error == null) CatgaDiagnostics.InboxLocksAcquired.Add(1);
            return ack.Error == null;
        }, cancellationToken);
    }

    public async ValueTask MarkAsProcessedAsync(InboxMessage message, CancellationToken cancellationToken = default)
    {
        await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Inbox.MarkProcessed", ActivityKind.Internal);
            ArgumentNullException.ThrowIfNull(message);
            await EnsureInitializedAsync(ct);

            message.ProcessedAt = DateTime.UtcNow;
            message.Status = InboxStatus.Processed;
            message.LockExpiresAt = null;

            var subject = $"{StreamName}.{message.MessageId}";
            var data = serializer.Serialize(message);
            var current = await GetMessageStateAsync(message.MessageId, ct);
            await JetStream.PublishAsync(
                subject,
                data,
                opts: new NatsJSPubOpts { ExpectedLastSubjectSequence = current?.Sequence ?? 0 },
                cancellationToken: ct);
            CatgaDiagnostics.InboxProcessed.Add(1);
        }, cancellationToken);
    }

    public async ValueTask<bool> HasBeenProcessedAsync(long messageId, CancellationToken cancellationToken = default)
    {
        return await provider.ExecutePersistenceAsync(async ct =>
        {
            await EnsureInitializedAsync(ct);
            var current = await GetMessageStateAsync(messageId, ct);
            return current is { } state && state.Message.Status == InboxStatus.Processed;
        }, cancellationToken);
    }

    public async ValueTask<byte[]?> GetProcessedResultAsync(long messageId, CancellationToken cancellationToken = default)
    {
        return await provider.ExecutePersistenceAsync(async ct =>
        {
            await EnsureInitializedAsync(ct);
            var current = await GetMessageStateAsync(messageId, ct);
            return current is { } state && state.Message.Status == InboxStatus.Processed ? state.Message.ProcessingResult : null;
        }, cancellationToken);
    }

    public async ValueTask ReleaseLockAsync(long messageId, CancellationToken cancellationToken = default)
    {
        await provider.ExecutePersistenceAsync(async ct =>
        {
            await EnsureInitializedAsync(ct);
            var current = await GetMessageStateAsync(messageId, ct);
            if (current is { } state)
            {
                state.Message.Status = InboxStatus.Pending;
                state.Message.LockExpiresAt = null;
                var subject = $"{StreamName}.{messageId}";
                var data = serializer.Serialize(state.Message);
                await JetStream.PublishAsync(
                    subject,
                    data,
                    opts: new NatsJSPubOpts { ExpectedLastSubjectSequence = state.Sequence },
                    cancellationToken: ct);
                CatgaDiagnostics.InboxLocksReleased.Add(1);
            }
        }, cancellationToken);
    }

    public ValueTask DeleteProcessedMessagesAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    private async Task<(InboxMessage Message, ulong Sequence)?> GetMessageStateAsync(long messageId, CancellationToken cancellationToken)
    {
        try
        {
            var subject = $"{StreamName}.{messageId}";
            var consumer = await JetStream.CreateOrUpdateConsumerAsync(StreamName,
                new ConsumerConfig { Name = $"inbox-get-{Guid.NewGuid():N}", FilterSubject = subject, AckPolicy = ConsumerConfigAckPolicy.None, DeliverPolicy = ConsumerConfigDeliverPolicy.LastPerSubject }, cancellationToken);

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

                    var message = (InboxMessage?)serializer.Deserialize(msg.Data, typeof(InboxMessage));
                    if (message != null && msg.Metadata is { Sequence.Stream: > 0 } metadata)
                    {
                        return (message, metadata.Sequence.Stream);
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
