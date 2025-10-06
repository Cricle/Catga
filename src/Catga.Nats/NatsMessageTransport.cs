using System.Diagnostics.CodeAnalysis;
using Catga.Serialization;
using Catga.Transport;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Catga.Nats;

/// <summary>
/// NATS 消息传输实现
/// </summary>
public class NatsMessageTransport : IMessageTransport
{
    private readonly INatsConnection _connection;
    private readonly IMessageSerializer _serializer;
    private readonly ILogger<NatsMessageTransport> _logger;
    private readonly string _subjectPrefix;

    public string Name => "NATS";

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
    }

    [RequiresUnreferencedCode("消息序列化可能需要无法静态分析的类型")]
    [RequiresDynamicCode("消息序列化可能需要运行时代码生成")]
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

        // 序列化消息
        var payload = _serializer.Serialize(message);

        // 创建 NATS 消息头
        var headers = new NatsHeaders
        {
            ["MessageId"] = context.MessageId,
            ["MessageType"] = context.MessageType ?? messageType.FullName!,
            ["SentAt"] = context.SentAt?.ToString("O") ?? DateTime.UtcNow.ToString("O")
        };

        if (!string.IsNullOrEmpty(context.CorrelationId))
            headers["CorrelationId"] = context.CorrelationId;

        // 发布到 NATS
        await _connection.PublishAsync(subject, payload, headers: headers, cancellationToken: cancellationToken);

        _logger.LogDebug("Published message {MessageId} to NATS subject {Subject}", context.MessageId, subject);
    }

    [RequiresUnreferencedCode("消息序列化可能需要无法静态分析的类型")]
    [RequiresDynamicCode("消息序列化可能需要运行时代码生成")]
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

    public async Task SubscribeAsync<TMessage>(
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
                // 反序列化消息
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message from subject {Subject}", subject);
            }
        }
    }

    private string GetSubject(Type messageType)
    {
        // 使用消息类型名称作为 subject
        var typeName = messageType.Name;
        return $"{_subjectPrefix}.{typeName}";
    }
}

/// <summary>
/// NATS 传输选项
/// </summary>
public class NatsTransportOptions
{
    /// <summary>
    /// Subject 前缀
    /// </summary>
    public string SubjectPrefix { get; set; } = "catga";
}

