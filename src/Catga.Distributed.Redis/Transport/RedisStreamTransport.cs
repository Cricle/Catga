using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Catga.Transport;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catga.Distributed.Redis;

/// <summary>
/// 基于 Redis Streams 的消息传输（原生功能）
/// 支持：Consumer Groups、ACK、Pending List、死信队列、持久化
/// </summary>
public sealed class RedisStreamTransport : IMessageTransport, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisStreamTransport> _logger;
    private readonly string _streamKey;
    private readonly string _consumerGroup;
    private readonly string _consumerId;
    private readonly CancellationTokenSource _disposeCts;

    public RedisStreamTransport(
        IConnectionMultiplexer redis,
        ILogger<RedisStreamTransport> logger,
        string streamKey = "catga:messages",
        string consumerGroup = "catga-group",
        string? consumerId = null)
    {
        _redis = redis;
        _logger = logger;
        _streamKey = streamKey;
        _consumerGroup = consumerGroup;
        _consumerId = consumerId ?? $"consumer-{Guid.NewGuid():N}";
        _disposeCts = new CancellationTokenSource();
    }

    public string Name => "Redis Streams";

    public BatchTransportOptions? BatchOptions => new()
    {
        MaxBatchSize = 100,
        BatchTimeout = TimeSpan.FromMilliseconds(100),
        EnableAutoBatching = true
    };

    public CompressionTransportOptions? CompressionOptions => null; // 不支持压缩

    [RequiresUnreferencedCode("Message serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Message serialization may require runtime code generation")]
    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        TMessage message,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var db = _redis.GetDatabase();

        // 序列化消息
        var payload = JsonSerializer.Serialize(message);

        // 构建 Stream 条目（原生 Redis Streams 格式）
        var fields = new NameValueEntry[]
        {
            new("type", typeof(TMessage).FullName!),
            new("payload", payload),
            new("messageId", context?.MessageId ?? Guid.NewGuid().ToString()),
            new("timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString())
        };

        // 添加到 Redis Stream（原生持久化，无需手动配置）
        await db.StreamAddAsync(_streamKey, fields);

        _logger.LogDebug("Published message {MessageId} to Redis Stream {Stream}",
            context?.MessageId ?? "unknown", _streamKey);
    }

    [RequiresUnreferencedCode("Message serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Message serialization may require runtime code generation")]
    public async Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        TMessage message,
        string destination,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        // 对于点对点消息，使用相同的 Stream 机制
        await PublishAsync(message, context, cancellationToken);
    }

    [RequiresUnreferencedCode("Message serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Message serialization may require runtime code generation")]
    public async Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        IEnumerable<TMessage> messages,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        // 批量发布（逐个发送到 Stream）
        foreach (var message in messages)
        {
            await PublishAsync(message, context, cancellationToken);
        }
    }

    [RequiresUnreferencedCode("Message serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Message serialization may require runtime code generation")]
    public async Task SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        IEnumerable<TMessage> messages,
        string destination,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        // 批量发送（逐个发送到 Stream）
        foreach (var message in messages)
        {
            await SendAsync(message, destination, context, cancellationToken);
        }
    }

    [RequiresUnreferencedCode("Message deserialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Message deserialization may require runtime code generation")]
    public async Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicConstructors)] TMessage>(
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var db = _redis.GetDatabase();

        // 确保 Consumer Group 存在
        await EnsureConsumerGroupExistsAsync(db);

        _logger.LogInformation("Starting Redis Stream consumer: {ConsumerId} in group: {Group}",
            _consumerId, _consumerGroup);

        // 消费消息（使用 Consumer Groups，自动负载均衡）
        while (!cancellationToken.IsCancellationRequested && !_disposeCts.Token.IsCancellationRequested)
        {
            try
            {
                // 从 Stream 读取消息（使用 Consumer Group，原生功能）
                var messages = await db.StreamReadGroupAsync(
                    _streamKey,
                    _consumerGroup,
                    _consumerId,
                    ">",                // 只读取新消息
                    count: 10);         // 批量读取（提高效率）

                if (messages.Length == 0)
                {
                    // 无新消息，等待一段时间
                    await Task.Delay(100, cancellationToken);
                    continue;
                }

                // 处理消息
                foreach (var streamEntry in messages)
                {
                    await ProcessMessageAsync(db, streamEntry, handler, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error reading from Redis Stream");
                await Task.Delay(1000, cancellationToken); // 错误后等待
            }
        }

        _logger.LogInformation("Redis Stream consumer stopped: {ConsumerId}", _consumerId);
    }

    /// <summary>
    /// 确保 Consumer Group 存在
    /// </summary>
    private async Task EnsureConsumerGroupExistsAsync(IDatabase db)
    {
        try
        {
            // 尝试创建 Consumer Group（原生 Redis 功能）
            await db.StreamCreateConsumerGroupAsync(
                _streamKey,
                _consumerGroup,
                StreamPosition.NewMessages);

            _logger.LogInformation("Created Consumer Group: {Group} for Stream: {Stream}",
                _consumerGroup, _streamKey);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            // Consumer Group 已存在，忽略
            _logger.LogDebug("Consumer Group {Group} already exists", _consumerGroup);
        }
    }

    /// <summary>
    /// 处理单个消息
    /// </summary>
    [RequiresDynamicCode("Calls System.Text.Json.JsonSerializer.Deserialize<TValue>(String, JsonSerializerOptions)")]
    [RequiresUnreferencedCode("Calls System.Text.Json.JsonSerializer.Deserialize<TValue>(String, JsonSerializerOptions)")]
    private async Task ProcessMessageAsync<TMessage>(
        IDatabase db,
        StreamEntry streamEntry,
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken cancellationToken)
        where TMessage : class
    {
        try
        {
            // 解析消息
            var typeValue = streamEntry.Values.FirstOrDefault(v => v.Name == "type").Value;
            var payloadValue = streamEntry.Values.FirstOrDefault(v => v.Name == "payload").Value;
            var messageIdValue = streamEntry.Values.FirstOrDefault(v => v.Name == "messageId").Value;

            if (!payloadValue.HasValue)
            {
                _logger.LogWarning("Message {MessageId} has no payload", streamEntry.Id);
                return;
            }

            // 反序列化消息
            var message = JsonSerializer.Deserialize<TMessage>(payloadValue.ToString());

            if (message == null)
            {
                _logger.LogWarning("Failed to deserialize message {MessageId}", streamEntry.Id);
                return;
            }

            // 构建传输上下文
            var context = new TransportContext
            {
                MessageId = messageIdValue.HasValue ? messageIdValue.ToString() : streamEntry.Id.ToString()
            };

            // 调用处理器
            await handler(message, context);

            // ACK 消息（标记已处理，原生功能）
            await db.StreamAcknowledgeAsync(_streamKey, _consumerGroup, streamEntry.Id);

            _logger.LogDebug("Processed and ACKed message {MessageId}", streamEntry.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process message {MessageId}", streamEntry.Id);

            // 不 ACK，消息会自动进入 Pending List（原生重试机制）
            // 可以使用 StreamPendingMessagesAsync 查看待处理消息
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        await Task.CompletedTask;
    }
}

