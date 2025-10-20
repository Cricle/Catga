using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Abstractions;
using Catga.Core;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;

namespace Catga.Transport.Nats;

/// <summary>NATS transport - delegates QoS to NATS native capabilities (Core Pub/Sub + JetStream)</summary>
/// <remarks>
/// QoS Mapping:
/// - QoS 0 (AtMostOnce): NATS Core Pub/Sub (fire-and-forget)
/// - QoS 1 (AtLeastOnce): JetStream with Ack (guaranteed delivery, may duplicate)
/// - QoS 2 (ExactlyOnce): JetStream with MsgId deduplication (exactly-once using NATS native dedup)
///
/// Catga responsibilities (via Pipeline Behaviors):
/// - Application-level idempotency (business logic dedup)
/// - Retry logic (with exponential backoff)
/// - Outbox/Inbox pattern (transactional outbox)
/// - Validation, caching, logging, tracing
/// </remarks>
public class NatsMessageTransport : IMessageTransport
{
    private readonly INatsConnection _connection;
    private readonly IMessageSerializer _serializer;
    private readonly ILogger<NatsMessageTransport> _logger;
    private readonly string _subjectPrefix;
    private INatsJSContext? _jsContext;

    public string Name => "NATS";
    public BatchTransportOptions? BatchOptions => null;
    public CompressionTransportOptions? CompressionOptions => null;

    public NatsMessageTransport(INatsConnection connection, IMessageSerializer serializer, ILogger<NatsMessageTransport> logger, NatsTransportOptions? options = null)
    {
        _connection = connection;
        _serializer = serializer;
        _logger = logger;
        _subjectPrefix = options?.SubjectPrefix ?? "catga";
        _jsContext = new NatsJSContext(_connection);
    }

    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
    {
        var subject = GetSubjectCached<TMessage>();
        var qos = (message as IMessage)?.QoS ?? QualityOfService.AtLeastOnce;
        var ctx = context ?? new TransportContext { MessageId = MessageExtensions.NewMessageId(), MessageType = TypeNameCache<TMessage>.FullName, SentAt = DateTime.UtcNow };

        var payload = _serializer.Serialize(message);
        var headers = new NatsHeaders
        {
            ["MessageId"] = ctx.MessageId?.ToString() ?? string.Empty,
            ["MessageType"] = ctx.MessageType ?? TypeNameCache<TMessage>.FullName,
            ["SentAt"] = ctx.SentAt?.ToString("O") ?? DateTime.UtcNow.ToString("O"),
            ["QoS"] = ((int)qos).ToString()
        };
        if (ctx.CorrelationId.HasValue)
            headers["CorrelationId"] = ctx.CorrelationId.Value.ToString();

        // Delegate QoS handling to NATS native capabilities
        switch (qos)
        {
            case QualityOfService.AtMostOnce:
                // NATS Core Pub/Sub: fire-and-forget, no ack, no persistence
                await _connection.PublishAsync(subject, payload, headers: headers, cancellationToken: cancellationToken);
                _logger.LogDebug("Published to NATS Core (QoS 0 - fire-and-forget): {MessageId}", ctx.MessageId);
                break;

            case QualityOfService.AtLeastOnce:
                // JetStream: guaranteed delivery, may duplicate (consumer acks)
                var ack1 = await _jsContext!.PublishAsync(subject: subject, data: payload, opts: new NatsJSPubOpts { MsgId = ctx.MessageId?.ToString() }, headers: headers, cancellationToken: cancellationToken);
                _logger.LogDebug("Published to JetStream (QoS 1 - at-least-once): {MessageId}, Seq: {Seq}, Duplicate: {Dup}", ctx.MessageId, ack1.Seq, ack1.Duplicate);
                break;

            case QualityOfService.ExactlyOnce:
                // JetStream with MsgId deduplication: exactly-once using NATS native dedup window (default 2 minutes)
                // Note: Application-level idempotency (via IdempotencyBehavior) provides additional business logic dedup
                var ack2 = await _jsContext!.PublishAsync(subject: subject, data: payload, opts: new NatsJSPubOpts { MsgId = ctx.MessageId?.ToString() }, headers: headers, cancellationToken: cancellationToken);
                _logger.LogDebug("Published to JetStream (QoS 2 - exactly-once): {MessageId}, Seq: {Seq}, Duplicate: {Dup}", ctx.MessageId, ack2.Seq, ack2.Duplicate);
                break;
        }
    }

    public Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message, string destination, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
        => PublishAsync(message, context, cancellationToken);

    public async Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(Func<TMessage, TransportContext, Task> handler, CancellationToken cancellationToken = default) where TMessage : class
    {
        var subject = GetSubjectCached<TMessage>();
        await foreach (var msg in _connection.SubscribeAsync<byte[]>(subject, cancellationToken: cancellationToken))
        {
            try
            {
                if (msg.Data == null || msg.Data.Length == 0)
                {
                    _logger.LogWarning("Received empty message from subject {Subject}", subject);
                    continue;
                }
                var message = _serializer.Deserialize<TMessage>(msg.Data);
                if (message == null)
                {
                    _logger.LogWarning("Failed to deserialize message from subject {Subject}", subject);
                    continue;
                }
                var sentAtValue = msg.Headers?["SentAt"];
                DateTime? sentAt = null;
                if (sentAtValue.HasValue && DateTime.TryParse(sentAtValue.Value.ToString(), out var parsed))
                    sentAt = parsed;
                long? messageId = null;
                if (msg.Headers?["MessageId"] is var msgIdHeader && long.TryParse(msgIdHeader.ToString(), out var parsedMsgId))
                    messageId = parsedMsgId;

                long? correlationId = null;
                if (msg.Headers?["CorrelationId"] is var corrIdHeader && long.TryParse(corrIdHeader.ToString(), out var parsedCorrId))
                    correlationId = parsedCorrId;

                var context = new TransportContext
                {
                    MessageId = messageId,
                    MessageType = msg.Headers?["MessageType"],
                    CorrelationId = correlationId,
                    SentAt = sentAt
                };
                await handler(message, context);
            }
            catch (Exception ex) { _logger.LogError(ex, "Error processing message from subject {Subject}", subject); }
        }
    }

    public async Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(IEnumerable<TMessage> messages, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
    {
        await BatchOperationHelper.ExecuteBatchAsync(
            messages,
            m => PublishAsync(m, context, cancellationToken));
    }

    public Task SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(IEnumerable<TMessage> messages, string destination, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
        => PublishBatchAsync(messages, context, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string GetSubjectCached<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>() => SubjectCache<TMessage>.Subject ??= $"{_subjectPrefix}.{TypeNameCache<TMessage>.Name}";
}

/// <summary>Zero-allocation subject cache per message type</summary>
internal static class SubjectCache<T>
{
    public static string? Subject;
}
