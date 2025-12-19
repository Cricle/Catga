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

    public async ValueTask<bool> TryLockMessageAsync(long messageId, TimeSpan lockDuration, CancellationToken cancellationToken = default)
    {
        // No retry for lock operations - they are not idempotent
        return await provider.ExecutePersistenceNoRetryAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Inbox.TryLock", ActivityKind.Internal);
            await EnsureInitializedAsync(ct);
            var subject = $"{StreamName}.{messageId}";

            var existing = await GetMessageAsync(messageId, ct);
            if (existing != null)
            {
                if (existing.Status == InboxStatus.Processed) return false;
                if (existing.LockExpiresAt.HasValue && existing.LockExpiresAt.Value > DateTime.UtcNow) return false;
            }

            var message = existing ?? new InboxMessage { MessageId = messageId, MessageType = string.Empty, Payload = [] };
            message.Status = InboxStatus.Processing;
            message.LockExpiresAt = DateTime.UtcNow.Add(lockDuration);

            var data = serializer.Serialize(message, typeof(InboxMessage));
            var ack = await JetStream.PublishAsync(subject, data, cancellationToken: ct);
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
            var data = serializer.Serialize(message, typeof(InboxMessage));
            await JetStream.PublishAsync(subject, data, cancellationToken: ct);
            CatgaDiagnostics.InboxProcessed.Add(1);
        }, cancellationToken);
    }

    public async ValueTask<bool> HasBeenProcessedAsync(long messageId, CancellationToken cancellationToken = default)
    {
        return await provider.ExecutePersistenceAsync(async ct =>
        {
            await EnsureInitializedAsync(ct);
            var message = await GetMessageAsync(messageId, ct);
            return message?.Status == InboxStatus.Processed;
        }, cancellationToken);
    }

    public async ValueTask<byte[]?> GetProcessedResultAsync(long messageId, CancellationToken cancellationToken = default)
    {
        return await provider.ExecutePersistenceAsync(async ct =>
        {
            await EnsureInitializedAsync(ct);
            var message = await GetMessageAsync(messageId, ct);
            return message?.Status == InboxStatus.Processed ? message.ProcessingResult : null;
        }, cancellationToken);
    }

    public async ValueTask ReleaseLockAsync(long messageId, CancellationToken cancellationToken = default)
    {
        await provider.ExecutePersistenceAsync(async ct =>
        {
            await EnsureInitializedAsync(ct);
            var message = await GetMessageAsync(messageId, ct);
            if (message != null)
            {
                message.Status = InboxStatus.Pending;
                message.LockExpiresAt = null;
                var subject = $"{StreamName}.{messageId}";
                var data = serializer.Serialize(message, typeof(InboxMessage));
                await JetStream.PublishAsync(subject, data, cancellationToken: ct);
                CatgaDiagnostics.InboxLocksReleased.Add(1);
            }
        }, cancellationToken);
    }

    public ValueTask DeleteProcessedMessagesAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    private async Task<InboxMessage?> GetMessageAsync(long messageId, CancellationToken cancellationToken)
    {
        try
        {
            var subject = $"{StreamName}.{messageId}";
            var consumer = await JetStream.CreateOrUpdateConsumerAsync(StreamName,
                new ConsumerConfig { Name = $"inbox-get-{Guid.NewGuid():N}", FilterSubject = subject, AckPolicy = ConsumerConfigAckPolicy.None, DeliverPolicy = ConsumerConfigDeliverPolicy.LastPerSubject }, cancellationToken);

            await foreach (var msg in consumer.FetchAsync<byte[]>(new NatsJSFetchOpts { MaxMsgs = 1 }, cancellationToken: cancellationToken))
                if (msg.Data is { Length: > 0 }) return (InboxMessage?)serializer.Deserialize(msg.Data, typeof(InboxMessage));
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404) { }
        return null;
    }
}
