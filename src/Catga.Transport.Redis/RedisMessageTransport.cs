using System.Diagnostics.CodeAnalysis;
using Catga.Serialization;
using Catga.Transport;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catga.Transport.Redis;

/// <summary>
/// Redis 消息传输实现 (使用 Pub/Sub)
/// </summary>
public class RedisMessageTransport : IMessageTransport
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageSerializer _serializer;
    private readonly ILogger<RedisMessageTransport> _logger;
    private readonly string _channelPrefix;

    public string Name => "Redis";

    public RedisMessageTransport(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        ILogger<RedisMessageTransport> logger,
        RedisTransportOptions? options = null)
    {
        _redis = redis;
        _serializer = serializer;
        _logger = logger;
        _channelPrefix = options?.ChannelPrefix ?? "catga";
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
        var channel = GetChannel(messageType);

        // 创建传输上下文
        context ??= new TransportContext
        {
            MessageId = Guid.NewGuid().ToString(),
            MessageType = messageType.FullName,
            SentAt = DateTime.UtcNow
        };

        // 构建传输消息（包含消息 + 上下文）
        var transportMessage = new RedisTransportMessage
        {
            MessageId = context.MessageId!,
            MessageType = context.MessageType ?? messageType.FullName!,
            CorrelationId = context.CorrelationId,
            SentAt = context.SentAt ?? DateTime.UtcNow,
            Payload = _serializer.Serialize(message),
            Metadata = context.Metadata
        };

        // 序列化传输消息
        var data = _serializer.Serialize(transportMessage);

        // 发布到 Redis
        var subscriber = _redis.GetSubscriber();
        await subscriber.PublishAsync(RedisChannel.Literal(channel), data);

        _logger.LogDebug("Published message {MessageId} to Redis channel {Channel}", context.MessageId, channel);
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
        // Redis Pub/Sub 不支持点对点，Send 等同于 Publish
        return PublishAsync(message, context, cancellationToken);
    }

    public Task SubscribeAsync<TMessage>(
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var messageType = typeof(TMessage);
        var channel = GetChannel(messageType);

        var subscriber = _redis.GetSubscriber();
        subscriber.Subscribe(RedisChannel.Literal(channel), async (_, data) =>
        {
            try
            {
                // 反序列化传输消息
                var transportMessage = _serializer.Deserialize<RedisTransportMessage>(data!);
                if (transportMessage == null)
                {
                    _logger.LogWarning("Failed to deserialize transport message from channel {Channel}", channel);
                    return;
                }

                // 反序列化实际消息
                var message = _serializer.Deserialize<TMessage>(transportMessage.Payload);
                if (message == null)
                {
                    _logger.LogWarning("Failed to deserialize message from channel {Channel}", channel);
                    return;
                }

                // 构建传输上下文
                var context = new TransportContext
                {
                    MessageId = transportMessage.MessageId,
                    MessageType = transportMessage.MessageType,
                    CorrelationId = transportMessage.CorrelationId,
                    SentAt = transportMessage.SentAt,
                    Metadata = transportMessage.Metadata
                };

                // 调用处理器
                await handler(message, context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message from channel {Channel}", channel);
            }
        });

        _logger.LogInformation("Subscribed to Redis channel {Channel} for message type {MessageType}",
            channel, messageType.Name);

        return Task.CompletedTask;
    }

    private string GetChannel(Type messageType)
    {
        // 使用消息类型名称作为 channel
        var typeName = messageType.Name;
        return $"{_channelPrefix}:{typeName}";
    }
}

/// <summary>
/// Redis 传输消息（包含消息 + 元数据）
/// </summary>
internal class RedisTransportMessage
{
    public required string MessageId { get; set; }
    public required string MessageType { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime SentAt { get; set; }
    public required byte[] Payload { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

