using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Catga.DeadLetter;
using Catga.Resilience;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Catga.Persistence.Nats;

/// <summary>NATS JetStream-based dead letter queue.</summary>
public sealed class NatsJSDeadLetterQueue(INatsConnection connection, IMessageSerializer serializer, IResiliencePipelineProvider provider, IOptions<NatsJSStoreOptions>? options = null)
    : NatsJSStoreBase(connection, options?.Value.DlqStreamName ?? "CATGA_DLQ", options?.Value), IDeadLetterQueue
{

    protected override string[] GetSubjects() => new[] { $"{StreamName.ToLowerInvariant()}.>" };

    public async Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        Exception exception,
        int retryCount,
        CancellationToken cancellationToken = default) where TMessage : IMessage
    {
        await provider.ExecutePersistenceAsync(async ct =>
        {
            await EnsureInitializedAsync(ct);

            var messageData = serializer.Serialize(message);

            var dlqMessage = new DeadLetterMessage
            {
                MessageId = message.MessageId,
                MessageType = TypeNameCache<TMessage>.Name,
                Message = messageData,
                ExceptionType = exception.GetType().Name,
                ExceptionMessage = exception.Message,
                StackTrace = exception.StackTrace ?? string.Empty,
                RetryCount = retryCount,
                FailedAt = DateTime.UtcNow
            };

            var subject = $"{StreamName.ToLowerInvariant()}.{message.MessageId}";
            var data = serializer.Serialize(dlqMessage);

            await JetStream.PublishAsync(subject, data, cancellationToken: ct);
        }, cancellationToken);
    }

    public async Task<List<DeadLetterMessage>> GetFailedMessagesAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default)
    {
        return await provider.ExecutePersistenceAsync(async ct =>
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
                            var dlqMsg = (DeadLetterMessage)serializer.Deserialize(msg.Data, typeof(DeadLetterMessage))!;
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
