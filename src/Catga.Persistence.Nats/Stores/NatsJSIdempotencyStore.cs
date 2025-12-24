using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Idempotency;
using Catga.Resilience;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Catga.Persistence.Nats;

/// <summary>NATS JetStream-based idempotency store.</summary>
public sealed class NatsJSIdempotencyStore(INatsConnection connection, IMessageSerializer serializer, IResiliencePipelineProvider provider, IOptions<NatsJSStoreOptions>? options = null)
    : NatsJSStoreBase(connection, options?.Value.IdempotencyStreamName ?? "CATGA_IDEMPOTENCY", options?.Value), IIdempotencyStore
{
    private readonly TimeSpan _ttl = options?.Value.IdempotencyTtl ?? TimeSpan.FromHours(24);

    protected override string[] GetSubjects() => new[] { $"{StreamName}.>" };

    protected override StreamConfig CreateStreamConfig()
    {
        // Override to add TTL configuration
        var config = base.CreateStreamConfig();
        config.MaxAge = _ttl;
        config.MaxMsgsPerSubject = 1; // Keep only latest for each messageId
        return config;
    }

    public async Task<bool> HasBeenProcessedAsync(long messageId, CancellationToken cancellationToken = default)
    {
        return await provider.ExecutePersistenceAsync(async ct =>
        {
            await EnsureInitializedAsync(ct);

            var subject = $"{StreamName}.{messageId}";

            try
            {
                // Try to get the last message for this subject
                var consumerName = $"check-{messageId}-{Guid.NewGuid():N}";
                var consumer = await JetStream.CreateOrUpdateConsumerAsync(
                    StreamName,
                    new ConsumerConfig
                    {
                        Name = consumerName,
                        FilterSubject = subject,
                        AckPolicy = ConsumerConfigAckPolicy.None,
                        DeliverPolicy = ConsumerConfigDeliverPolicy.LastPerSubject
                    },
                    ct);

                await foreach (var msg in consumer.FetchAsync<byte[]>(
                    new NatsJSFetchOpts { MaxMsgs = 1 },
                    cancellationToken: ct))
                {
                    // Message exists
                    return true;
                }

                return false;
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404)
            {
                // Consumer or stream doesn't exist
                return false;
            }
        }, cancellationToken);
    }

    public async Task MarkAsProcessedAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(
        long messageId,
        TResult? result = default,
        CancellationToken cancellationToken = default)
    {
        await provider.ExecutePersistenceAsync(async ct =>
        {
            await EnsureInitializedAsync(ct);

            var subject = $"{StreamName}.{messageId}";
            byte[] data;

            if (result != null)
            {
                data = serializer.Serialize(result);
            }
            else
            {
                // Empty marker
                data = Array.Empty<byte>();
            }

            var headers = new NatsHeaders
            {
                ["HasResult"] = result != null ? "true" : "false"
            };

            var ack = await JetStream.PublishAsync(subject, data, headers: headers, cancellationToken: ct);

            if (ack.Error != null)
            {
                throw new InvalidOperationException($"Failed to mark message as processed: {ack.Error.Description}");
            }
        }, cancellationToken);
    }

    public async Task<TResult?> GetCachedResultAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(
        long messageId,
        CancellationToken cancellationToken = default)
    {
        return await provider.ExecutePersistenceAsync(async ct =>
        {
            await EnsureInitializedAsync(ct);

            var subject = $"{StreamName}.{messageId}";

            try
            {
                var consumerName = $"get-{messageId}-{Guid.NewGuid():N}";
                var consumer = await JetStream.CreateOrUpdateConsumerAsync(
                    StreamName,
                    new ConsumerConfig
                    {
                        Name = consumerName,
                        FilterSubject = subject,
                        AckPolicy = ConsumerConfigAckPolicy.None,
                        DeliverPolicy = ConsumerConfigDeliverPolicy.LastPerSubject
                    },
                    ct);

                await foreach (var msg in consumer.FetchAsync<byte[]>(
                    new NatsJSFetchOpts { MaxMsgs = 1 },
                    cancellationToken: ct))
                {
                    var hasResultHeader = msg.Headers?["HasResult"];
                    var hasResult = hasResultHeader.HasValue && hasResultHeader.ToString() == "true";

                    if (!hasResult || msg.Data == null || msg.Data.Length == 0)
                        return default;

                    return (TResult?)serializer.Deserialize(msg.Data, typeof(TResult));
                }

                return default;
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404)
            {
                return default;
            }
        }, cancellationToken);
    }
}

