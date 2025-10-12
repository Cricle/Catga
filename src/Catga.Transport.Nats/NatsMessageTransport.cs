using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Catga.Core;
using Catga.Messages;
using Catga.Serialization;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;

namespace Catga.Transport.Nats;

/// <summary>NATS transport with QoS support (QoS 0: Core Pub/Sub, QoS 1/2: JetStream)</summary>
public class NatsMessageTransport : IMessageTransport
{
    private readonly INatsConnection _connection;
    private readonly IMessageSerializer _serializer;
    private readonly ILogger<NatsMessageTransport> _logger;
    private readonly string _subjectPrefix;
    private readonly ConcurrentDictionary<string, bool> _processedMessages = new();
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

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "序列化警告已在接口层标记")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "序列化警告已在接口层标记")]
    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
    {
        var subject = GetSubjectCached<TMessage>();
        var qos = (message as IMessage)?.QoS ?? QualityOfService.AtLeastOnce;
        var ctx = context ?? new TransportContext { MessageId = Guid.NewGuid().ToString(), MessageType = TypeNameCache<TMessage>.FullName, SentAt = DateTime.UtcNow };

        if (qos == QualityOfService.ExactlyOnce && ctx.MessageId != null && _processedMessages.ContainsKey(ctx.MessageId))
        {
            _logger.LogDebug("Message {MessageId} already processed (QoS 2), skipping", ctx.MessageId);
            return;
        }

        var payload = _serializer.Serialize(message);
        var headers = new NatsHeaders
        {
            ["MessageId"] = ctx.MessageId,
            ["MessageType"] = ctx.MessageType ?? TypeNameCache<TMessage>.FullName,
            ["SentAt"] = ctx.SentAt?.ToString("O") ?? DateTime.UtcNow.ToString("O"),
            ["QoS"] = ((int)qos).ToString()
        };
        if (!string.IsNullOrEmpty(ctx.CorrelationId))
            headers["CorrelationId"] = ctx.CorrelationId;

        switch (qos)
        {
            case QualityOfService.AtMostOnce:
                await _connection.PublishAsync(subject, payload, headers: headers, cancellationToken: cancellationToken);
                _logger.LogDebug("Published message {MessageId} to NATS Core (QoS 0)", ctx.MessageId);
                break;

            case QualityOfService.AtLeastOnce:
                var ack = await _jsContext!.PublishAsync(subject: subject, data: payload, opts: new NatsJSPubOpts { MsgId = ctx.MessageId }, headers: headers, cancellationToken: cancellationToken);
                if (ack.Duplicate)
                    _logger.LogDebug("Message {MessageId} is duplicate, JetStream auto-deduplicated", ctx.MessageId);
                else
                    _logger.LogDebug("Message {MessageId} published to JetStream (QoS 1), Seq: {Seq}", ctx.MessageId, ack.Seq);
                break;

            case QualityOfService.ExactlyOnce:
                if (ctx.MessageId != null && _processedMessages.ContainsKey(ctx.MessageId))
                {
                    _logger.LogDebug("Message {MessageId} already processed locally (QoS 2), skipping", ctx.MessageId);
                    return;
                }
                var ack2 = await _jsContext!.PublishAsync(subject: subject, data: payload, opts: new NatsJSPubOpts { MsgId = ctx.MessageId }, headers: headers, cancellationToken: cancellationToken);
                if (!string.IsNullOrEmpty(ctx.MessageId))
                    _processedMessages.TryAdd(ctx.MessageId, true);
                _logger.LogDebug("Message {MessageId} published to JetStream (QoS 2), Duplicate: {Dup}, Seq: {Seq}", ctx.MessageId, ack2.Duplicate, ack2.Seq);
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
                var context = new TransportContext
                {
                    MessageId = msg.Headers?["MessageId"],
                    MessageType = msg.Headers?["MessageType"],
                    CorrelationId = msg.Headers?["CorrelationId"],
                    SentAt = sentAt
                };
                await handler(message, context);
            }
            catch (Exception ex) { _logger.LogError(ex, "Error processing message from subject {Subject}", subject); }
        }
    }

    public async Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(IEnumerable<TMessage> messages, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
    {
        foreach (var message in messages)
            await PublishAsync(message, context, cancellationToken);
    }

    public Task SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(IEnumerable<TMessage> messages, string destination, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
        => PublishBatchAsync(messages, context, cancellationToken);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private string GetSubjectCached<TMessage>() => SubjectCache<TMessage>.Subject ??= $"{_subjectPrefix}.{TypeNameCache<TMessage>.Name}";

    private string GetSubject(Type messageType) => $"{_subjectPrefix}.{messageType.Name}";
}

/// <summary>Zero-allocation subject cache per message type</summary>
internal static class SubjectCache<T>
{
    public static string? Subject;
}
