using System;
using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Catga.DeadLetter;
using Catga.Persistence;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Catga.Resilience;

namespace Catga.Persistence.Nats;

/// <summary>
/// NATS JetStream-based dead letter queue (lock-free, uses JetStream)
/// </summary>
/// <remarks>
/// Lock-free: NATS JetStream handles all concurrency internally.
/// Uses dedicated stream for dead letter messages.
/// AOT-compatible: uses IMessageSerializer interface.
/// </remarks>
public sealed class NatsJSDeadLetterQueue : NatsJSStoreBase, IDeadLetterQueue
{
    private readonly IMessageSerializer _serializer;
    private readonly IResiliencePipelineProvider _provider;

    public NatsJSDeadLetterQueue(
        INatsConnection connection,
        IMessageSerializer serializer,
        string streamName = "CATGA_DLQ",
        NatsJSStoreOptions? options = null,
        IResiliencePipelineProvider? provider = null)
        : base(connection, streamName, options)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    protected override string[] GetSubjects() => new[] { $"{StreamName.ToLowerInvariant()}.>" };

    public async Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        Exception exception,
        int retryCount,
        CancellationToken cancellationToken = default) where TMessage : IMessage
    {
        await _provider.ExecutePersistenceAsync(async ct =>
        {
            await EnsureInitializedAsync(ct);

            var messageData = Convert.ToBase64String(_serializer.Serialize(message, typeof(TMessage)));

            var dlqMessage = new DeadLetterMessage
            {
                MessageId = message.MessageId,
                MessageType = TypeNameCache<TMessage>.Name,
                MessageJson = messageData,
                ExceptionType = ExceptionTypeCache.GetTypeName(exception),
                ExceptionMessage = exception.Message,
                StackTrace = exception.StackTrace ?? string.Empty,
                RetryCount = retryCount,
                FailedAt = DateTime.UtcNow
            };

            var subject = $"{StreamName.ToLowerInvariant()}.{message.MessageId}";
            var data = _serializer.Serialize(dlqMessage, typeof(DeadLetterMessage));

            await JetStream.PublishAsync(subject, data, cancellationToken: ct);
        }, cancellationToken);
    }

    public async Task<List<DeadLetterMessage>> GetFailedMessagesAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default)
    {
        return await _provider.ExecutePersistenceAsync(async ct =>
        {
            await EnsureInitializedAsync(ct);

            var result = new List<DeadLetterMessage>();

            try
            {
                var consumer = await JetStream.CreateOrUpdateConsumerAsync(
                    StreamName,
                    new ConsumerConfig
                    {
                        Name = $"{StreamName}_reader",
                        AckPolicy = ConsumerConfigAckPolicy.Explicit,
                        MaxAckPending = maxCount
                    },
                    ct);

                var count = 0;
                await foreach (var msg in consumer.FetchAsync<byte[]>(opts: new NatsJSFetchOpts { MaxMsgs = maxCount }, cancellationToken: ct))
                {
                    if (count >= maxCount)
                        break;

                    try
                    {
                        if (msg.Data != null)
                        {
                            var dlqMsg = (DeadLetterMessage)_serializer.Deserialize(msg.Data, typeof(DeadLetterMessage))!;
                            result.Add(dlqMsg);
                            await msg.AckAsync(cancellationToken: ct);
                            count++;
                        }
                    }
                    catch
                    {
                        await msg.NakAsync(cancellationToken: ct);
                    }
                }
            }
            catch (NatsJSApiException)
            {
                // Consumer doesn't exist or stream is empty, return empty list
            }

            return result;
        }, cancellationToken);
    }
}
