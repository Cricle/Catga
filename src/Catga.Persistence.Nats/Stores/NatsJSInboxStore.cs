using Catga.Abstractions;
using Catga.Inbox;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Catga.Resilience;
using System.Diagnostics;
using Catga.Observability;

namespace Catga.Persistence.Stores;

/// <summary>
/// NATS JetStream-based inbox store for idempotent message processing
/// </summary>
public sealed class NatsJSInboxStore : NatsJSStoreBase, IInboxStore
{
    private readonly IMessageSerializer _serializer;
    private readonly IResiliencePipelineProvider _provider;

    public NatsJSInboxStore(
        INatsConnection connection,
        IMessageSerializer serializer,
        string? streamName = null,
        NatsJSStoreOptions? options = null,
        IResiliencePipelineProvider? provider = null)
        : base(connection, streamName ?? "CATGA_INBOX", options)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    protected override string[] GetSubjects() => [$"{StreamName}.>"];

    public async ValueTask<bool> TryLockMessageAsync(
        long messageId,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
    {
        return await _provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Inbox.TryLock", ActivityKind.Internal);
            await EnsureInitializedAsync(ct);

            var subject = $"{StreamName}.{messageId}";

            // Check if message already exists and is processed
            var existing = await GetMessageAsync(messageId, ct);
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

            var data = _serializer.Serialize(message, typeof(InboxMessage));
            var ack = await JetStream.PublishAsync(subject, data, cancellationToken: ct);

            var ok = ack.Error == null;
            if (ok) CatgaDiagnostics.InboxLocksAcquired.Add(1);
            return ok;
        }, cancellationToken);
    }

    public async ValueTask MarkAsProcessedAsync(
        InboxMessage message,
        CancellationToken cancellationToken = default)
    {
        await _provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Inbox.MarkProcessed", ActivityKind.Internal);
            ArgumentNullException.ThrowIfNull(message);

            await EnsureInitializedAsync(ct);

            message.ProcessedAt = DateTime.UtcNow;
            message.Status = InboxStatus.Processed;
            message.LockExpiresAt = null;

            var subject = $"{StreamName}.{message.MessageId}";
            var data = _serializer.Serialize(message, typeof(InboxMessage));

            await JetStream.PublishAsync(subject, data, cancellationToken: ct);
            CatgaDiagnostics.InboxProcessed.Add(1);
        }, cancellationToken);
    }

    public async ValueTask<bool> HasBeenProcessedAsync(
        long messageId,
        CancellationToken cancellationToken = default)
    {
        return await _provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Inbox.HasBeenProcessed", ActivityKind.Internal);
            await EnsureInitializedAsync(ct);

            var message = await GetMessageAsync(messageId, ct);
            return message?.Status == InboxStatus.Processed;
        }, cancellationToken);
    }

    public async ValueTask<string?> GetProcessedResultAsync(
        long messageId,
        CancellationToken cancellationToken = default)
    {
        return await _provider.ExecutePersistenceAsync(async ct =>
        {
            await EnsureInitializedAsync(ct);

            var message = await GetMessageAsync(messageId, ct);
            return message?.Status == InboxStatus.Processed ? message.ProcessingResult : null;
        }, cancellationToken);
    }

    public async ValueTask ReleaseLockAsync(
        long messageId,
        CancellationToken cancellationToken = default)
    {
        await _provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Inbox.ReleaseLock", ActivityKind.Internal);
            await EnsureInitializedAsync(ct);

            var message = await GetMessageAsync(messageId, ct);
            if (message != null)
            {
                message.Status = InboxStatus.Pending;
                message.LockExpiresAt = null;

                var subject = $"{StreamName}.{messageId}";
                var data = _serializer.Serialize(message, typeof(InboxMessage));

                await JetStream.PublishAsync(subject, data, cancellationToken: ct);
                CatgaDiagnostics.InboxLocksReleased.Add(1);
            }
        }, cancellationToken);
    }

    public async ValueTask DeleteProcessedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default)
    {
        await _provider.ExecutePersistenceAsync(ct => new ValueTask(), cancellationToken);
    }

    private async Task<InboxMessage?> GetMessageAsync(long messageId, CancellationToken cancellationToken)
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
                    return (InboxMessage?)_serializer.Deserialize(msg.Data, typeof(InboxMessage));
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
