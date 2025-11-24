using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Catga.DeadLetter;
using Catga.Persistence;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

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

    public NatsJSDeadLetterQueue(
        INatsConnection connection,
        IMessageSerializer serializer,
        string streamName = "CATGA_DLQ",
        NatsJSStoreOptions? options = null)
        : base(connection, streamName, options)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    protected override string[] GetSubjects() => new[] { $"{StreamName.ToLowerInvariant()}.>" };

    public async Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        Exception exception,
        int retryCount,
        CancellationToken cancellationToken = default) where TMessage : IMessage
    {
        await EnsureInitializedAsync(cancellationToken);

        var messageJson = _serializer.SerializeToJson(message);

        var dlqMessage = new DeadLetterMessage
        {
            MessageId = message.MessageId,
            MessageType = TypeNameCache<TMessage>.Name,
            MessageJson = messageJson,
            ExceptionType = ExceptionTypeCache.GetTypeName(exception),
            ExceptionMessage = exception.Message,
            StackTrace = exception.StackTrace ?? string.Empty,
            RetryCount = retryCount,
            FailedAt = DateTime.UtcNow
        };

        var subject = $"{StreamName.ToLowerInvariant()}.{message.MessageId}";
        var data = _serializer.Serialize(dlqMessage, typeof(DeadLetterMessage));

        await JetStream.PublishAsync(subject, data, cancellationToken: cancellationToken);
    }

    public async Task<List<DeadLetterMessage>> GetFailedMessagesAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

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
                cancellationToken);

            var count = 0;
            await foreach (var msg in consumer.FetchAsync<byte[]>(opts: new NatsJSFetchOpts { MaxMsgs = maxCount }, cancellationToken: cancellationToken))
            {
                if (count >= maxCount)
                    break;

                try
                {
                    if (msg.Data != null)
                    {
                        var dlqMsg = (DeadLetterMessage)_serializer.Deserialize(msg.Data, typeof(DeadLetterMessage))!;
                        result.Add(dlqMsg);
                        await msg.AckAsync(cancellationToken: cancellationToken);
                        count++;
                    }
                }
                catch
                {
                    await msg.NakAsync(cancellationToken: cancellationToken);
                }
            }
        }
        catch (NatsJSApiException)
        {
            // Consumer doesn't exist or stream is empty, return empty list
        }

        return result;
    }
}
