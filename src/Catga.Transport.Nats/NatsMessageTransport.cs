using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
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
        var messageType = typeof(TMessage);
        var subject = GetSubject(messageType);
        context ??= new TransportContext { MessageId = Guid.NewGuid().ToString(), MessageType = messageType.FullName, SentAt = DateTime.UtcNow };
        var qos = (message as IMessage)?.QoS ?? QualityOfService.AtLeastOnce;

        if (qos == QualityOfService.ExactlyOnce && context.MessageId != null && _processedMessages.ContainsKey(context.MessageId))
        {
            _logger.LogDebug("Message {MessageId} already processed (QoS 2), skipping", context.MessageId);
            return;
        }

        var payload = _serializer.Serialize(message);
        var headers = new NatsHeaders
        {
            ["MessageId"] = context.MessageId,
            ["MessageType"] = context.MessageType ?? messageType.FullName!,
            ["SentAt"] = context.SentAt?.ToString("O") ?? DateTime.UtcNow.ToString("O"),
            ["QoS"] = ((int)qos).ToString()
        };
        if (!string.IsNullOrEmpty(context.CorrelationId))
            headers["CorrelationId"] = context.CorrelationId;

        switch (qos)
        {
            case QualityOfService.AtMostOnce:
                await _connection.PublishAsync(subject, payload, headers: headers, cancellationToken: cancellationToken);
                _logger.LogDebug("Published message {MessageId} to NATS Core (QoS 0)", context.MessageId);
                break;

            case QualityOfService.AtLeastOnce:
                var ack = await _jsContext!.PublishAsync(subject: subject, data: payload, opts: new NatsJSPubOpts { MsgId = context.MessageId }, headers: headers, cancellationToken: cancellationToken);
                if (ack.Duplicate)
                    _logger.LogDebug("Message {MessageId} is duplicate, JetStream auto-deduplicated", context.MessageId);
                else
                    _logger.LogDebug("Message {MessageId} published to JetStream (QoS 1), Seq: {Seq}", context.MessageId, ack.Seq);
                break;

            case QualityOfService.ExactlyOnce:
                if (context.MessageId != null && _processedMessages.ContainsKey(context.MessageId))
                {
                    _logger.LogDebug("Message {MessageId} already processed locally (QoS 2), skipping", context.MessageId);
                    return;
                }
                var ack2 = await _jsContext!.PublishAsync(subject: subject, data: payload, opts: new NatsJSPubOpts { MsgId = context.MessageId }, headers: headers, cancellationToken: cancellationToken);
                if (!string.IsNullOrEmpty(context.MessageId))
                    _processedMessages.TryAdd(context.MessageId, true);
                _logger.LogDebug("Message {MessageId} published to JetStream (QoS 2), Duplicate: {Dup}, Seq: {Seq}", context.MessageId, ack2.Duplicate, ack2.Seq);
                break;
        }
    }

    public Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message, string destination, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
        => PublishAsync(message, context, cancellationToken);

    public async Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(Func<TMessage, TransportContext, Task> handler, CancellationToken cancellationToken = default) where TMessage : class
    {
        var messageType = typeof(TMessage);
        var subject = GetSubject(messageType);
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
                var context = new TransportContext
                {
                    MessageId = msg.Headers?["MessageId"],
                    MessageType = msg.Headers?["MessageType"],
                    CorrelationId = msg.Headers?["CorrelationId"]
                };
                var sentAtValue = msg.Headers?["SentAt"];
                if (sentAtValue.HasValue && DateTime.TryParse(sentAtValue.Value.ToString(), out var sentAt))
                    context.SentAt = sentAt;
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

    private string GetSubject(Type messageType) => $"{_subjectPrefix}.{messageType.Name}";
}
