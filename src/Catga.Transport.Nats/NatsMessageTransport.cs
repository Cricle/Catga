using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Catga.Messages;
using Catga.Serialization;
using Catga.Transport;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;

namespace Catga.Transport.Nats;

/// <summary>
/// NATS 消息传输实现（支持 QoS）
/// QoS 0: NATS Core Pub/Sub (fire-and-forget)
/// QoS 1/2: NATS JetStream (原生 ACK + 持久化)
/// </summary>
public class NatsMessageTransport : IMessageTransport
{
    private readonly INatsConnection _connection;
    private readonly IMessageSerializer _serializer;
    private readonly ILogger<NatsMessageTransport> _logger;
    private readonly string _subjectPrefix;
    private readonly ConcurrentDictionary<string, bool> _processedMessages = new();
    private INatsJSContext? _jsContext;

    public string Name => "NATS";

    public BatchTransportOptions? BatchOptions => null;  // NATS handles batching internally

    public CompressionTransportOptions? CompressionOptions => null;  // Compression handled at NATS level

    public NatsMessageTransport(
        INatsConnection connection,
        IMessageSerializer serializer,
        ILogger<NatsMessageTransport> logger,
        NatsTransportOptions? options = null)
    {
        _connection = connection;
        _serializer = serializer;
        _logger = logger;
        _subjectPrefix = options?.SubjectPrefix ?? "catga";

        // 初始化 JetStream Context（用于 QoS 1/2）
        _jsContext = new NatsJSContext(_connection);
    }
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "序列化警告已在接口层标记")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "序列化警告已在接口层标记")]
    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        TMessage message,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var messageType = typeof(TMessage);
        var subject = GetSubject(messageType);

        // 创建传输上下文
        context ??= new TransportContext
        {
            MessageId = Guid.NewGuid().ToString(),
            MessageType = messageType.FullName,
            SentAt = DateTime.UtcNow
        };

        // Get QoS level
        var qos = (message as IMessage)?.QoS ?? QualityOfService.AtLeastOnce;

        // QoS 2: Check if already processed
        if (qos == QualityOfService.ExactlyOnce && context.MessageId != null)
        {
            if (_processedMessages.ContainsKey(context.MessageId))
            {
                _logger.LogDebug("Message {MessageId} already processed (QoS 2), skipping", context.MessageId);
                return;
            }
        }

        // 序列化消息
        var payload = _serializer.Serialize(message);

        // 创建 NATS 消息头
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
                // QoS 0: Fire-and-forget (NATS Core Pub/Sub - 最快)
                await _connection.PublishAsync(subject, payload, headers: headers, cancellationToken: cancellationToken);
                _logger.LogDebug("Published message {MessageId} to NATS Core (QoS 0 - fire-and-forget)", context.MessageId);
                break;

            case QualityOfService.AtLeastOnce:
                // QoS 1: JetStream Publish（原生 ACK + 持久化）
                var ack = await _jsContext!.PublishAsync(
                    subject: subject,
                    data: payload,
                    opts: new NatsJSPubOpts
                    {
                        MsgId = context.MessageId // 用于去重
                    },
                    headers: headers,
                    cancellationToken: cancellationToken);

                if (ack.Duplicate)
                {
                    _logger.LogDebug("Message {MessageId} is duplicate, JetStream auto-deduplicated", context.MessageId);
                }
                else
                {
                    _logger.LogDebug("Message {MessageId} published to JetStream (QoS 1 - at-least-once with native ACK), Seq: {Seq}",
                        context.MessageId, ack.Seq);
                }
                break;

            case QualityOfService.ExactlyOnce:
                // QoS 2: JetStream + 应用层去重
                if (context.MessageId != null && _processedMessages.ContainsKey(context.MessageId))
                {
                    _logger.LogDebug("Message {MessageId} already processed locally (QoS 2), skipping", context.MessageId);
                    return;
                }

                var ack2 = await _jsContext!.PublishAsync(
                    subject: subject,
                    data: payload,
                    opts: new NatsJSPubOpts
                    {
                        MsgId = context.MessageId // JetStream 原生去重
                    },
                    headers: headers,
                    cancellationToken: cancellationToken);

                // 应用层去重（双重保障）
                if (!string.IsNullOrEmpty(context.MessageId))
                {
                    _processedMessages.TryAdd(context.MessageId, true);
                }

                _logger.LogDebug("Message {MessageId} published to JetStream (QoS 2 - exactly-once), Duplicate: {Dup}, Seq: {Seq}",
                    context.MessageId, ack2.Duplicate, ack2.Seq);
                break;
        }
    }
    public Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        TMessage message,
        string destination,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        // NATS 是 pub/sub 模型，Send 直接使用指定的 subject
        return PublishAsync(message, context, cancellationToken);
    }
    public async Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicConstructors)] TMessage>(
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var messageType = typeof(TMessage);
        var subject = GetSubject(messageType);

        await foreach (var msg in _connection.SubscribeAsync<byte[]>(subject, cancellationToken: cancellationToken))
        {
            try
            {
                // Validate message data
                if (msg.Data == null || msg.Data.Length == 0)
                {
                    _logger.LogWarning("Received empty message from subject {Subject}", subject);
                    continue;
                }

                // Deserialize message
                var message = _serializer.Deserialize<TMessage>(msg.Data);
                if (message == null)
                {
                    _logger.LogWarning("Failed to deserialize message from subject {Subject}", subject);
                    continue;
                }

                // 构建传输上下文
                var context = new TransportContext
                {
                    MessageId = msg.Headers?["MessageId"],
                    MessageType = msg.Headers?["MessageType"],
                    CorrelationId = msg.Headers?["CorrelationId"]
                };

                var sentAtValue = msg.Headers?["SentAt"];
                if (sentAtValue.HasValue && DateTime.TryParse(sentAtValue.Value.ToString(), out var sentAt))
                    context.SentAt = sentAt;

                // 调用处理器
                await handler(message, context);

                // 注意：JetStream 消息的 ACK 由 Consumer 自动处理
                // NATS Core 消息不需要 ACK
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message from subject {Subject}", subject);
            }
        }
    }
    public async Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        IEnumerable<TMessage> messages,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        // NATS doesn't have native batch support, publish each message individually
        foreach (var message in messages)
        {
            await PublishAsync(message, context, cancellationToken);
        }
    }
    public Task SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        IEnumerable<TMessage> messages,
        string destination,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        // For NATS, Send and Publish are the same (pub/sub model)
        return PublishBatchAsync(messages, context, cancellationToken);
    }

    private string GetSubject(Type messageType)
    {
        // 使用消息类型名称作为 subject
        var typeName = messageType.Name;
        return $"{_subjectPrefix}.{typeName}";
    }
}

