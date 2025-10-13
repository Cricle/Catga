using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Catga.Core;
using Catga.Messages;
using Catga.Transport;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catga.Distributed.Redis;

/// <summary>
/// Redis Streams transport with QoS support
/// - QoS 0 (AtMostOnce): Fire and forget, no ACK
/// - QoS 1 (AtLeastOnce): Consumer Groups + ACK + Pending List retry
/// - QoS 2 (ExactlyOnce): Consumer Groups + ACK + Idempotency (via Catga's Inbox)
/// </summary>
public sealed class RedisStreamTransport : IMessageTransport, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisStreamTransport> _logger;
    private readonly string _streamKey;
    private readonly string _consumerGroup;
    private readonly string _consumerId;
    private readonly CancellationTokenSource _disposeCts;
    private readonly RedisStreamOptions _options;

    public RedisStreamTransport(
        IConnectionMultiplexer redis,
        ILogger<RedisStreamTransport> logger,
        RedisStreamOptions? options = null)
    {
        _redis = redis;
        _logger = logger;
        _options = options ?? new RedisStreamOptions();
        _streamKey = _options.StreamKey;
        _consumerGroup = _options.ConsumerGroup;
        _consumerId = _options.ConsumerId ?? $"consumer-{Guid.NewGuid():N}";
        _disposeCts = new CancellationTokenSource();
    }

    public string Name => "Redis Streams";
    public BatchTransportOptions? BatchOptions => new() { MaxBatchSize = 100, BatchTimeout = TimeSpan.FromMilliseconds(100), EnableAutoBatching = true };
    public CompressionTransportOptions? CompressionOptions => null;

    [RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("JSON serialization may require runtime code generation")]
    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
    {
        var db = _redis.GetDatabase();
        var payload = JsonSerializer.Serialize(message);

        // 提取 QoS（如果消息实现了 IMessage）
        var qos = (message as IMessage)?.QoS ?? QualityOfService.AtMostOnce;

        var fields = new NameValueEntry[]
        {
            new("type", TypeNameCache<TMessage>.FullName),
            new("payload", payload),
            new("messageId", context?.MessageId ?? Guid.NewGuid().ToString()),
            new("qos", ((int)qos).ToString()),
            new("retryCount", "0"),
            new("timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString())
        };

        await db.StreamAddAsync(_streamKey, fields);
        _logger.LogDebug("Published message {MessageId} to Redis Stream {Stream} with QoS={QoS}",
            context?.MessageId ?? "unknown", _streamKey, qos);
    }

    public async Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message, string destination, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
        => await PublishAsync(message, context, cancellationToken);

    public async Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(IEnumerable<TMessage> messages, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
    {
        foreach (var message in messages)
            await PublishAsync(message, context, cancellationToken);
    }

    public async Task SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(IEnumerable<TMessage> messages, string destination, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
    {
        foreach (var message in messages)
            await SendAsync(message, destination, context, cancellationToken);
    }

    public async Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(Func<TMessage, TransportContext, Task> handler, CancellationToken cancellationToken = default) where TMessage : class
    {
        var db = _redis.GetDatabase();
        await EnsureConsumerGroupExistsAsync(db);
        _logger.LogInformation("Starting Redis Stream consumer: {ConsumerId} in group: {Group} with QoS support", _consumerId, _consumerGroup);

        // 启动 Pending List 处理任务（QoS 1 重试）
        var pendingTask = ProcessPendingMessagesAsync<TMessage>(db, handler, cancellationToken);

        while (!cancellationToken.IsCancellationRequested && !_disposeCts.Token.IsCancellationRequested)
        {
            try
            {
                // 1. 读取新消息
                var messages = await db.StreamReadGroupAsync(_streamKey, _consumerGroup, _consumerId, ">", count: 10);
                if (messages.Length == 0)
                {
                    await Task.Delay(100, cancellationToken);
                    continue;
                }

                foreach (var streamEntry in messages)
                    await ProcessMessageAsync(db, streamEntry, handler, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error reading from Redis Stream");
                await Task.Delay(1000, cancellationToken);
            }
        }

        _logger.LogInformation("Redis Stream consumer stopped: {ConsumerId}", _consumerId);
        await pendingTask;
    }

    /// <summary>
    /// 处理 Pending List 中的消息（QoS 1 重试机制）
    /// </summary>
    private async Task ProcessPendingMessagesAsync<TMessage>(IDatabase db, Func<TMessage, TransportContext, Task> handler, CancellationToken cancellationToken) where TMessage : class
    {
        while (!cancellationToken.IsCancellationRequested && !_disposeCts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.PendingCheckInterval, cancellationToken);

                // 获取 Pending List（超过 MinIdleTime 的消息）
                var pending = await db.StreamPendingMessagesAsync(
                    _streamKey,
                    _consumerGroup,
                    count: 10,
                    _consumerId);

                if (pending.Length == 0)
                    continue;

                _logger.LogInformation("Found {Count} pending messages to retry", pending.Length);

                foreach (var pendingMessage in pending)
                {
                    // 检查消息是否超过最小空闲时间
                    if (pendingMessage.IdleTimeInMilliseconds < _options.MinIdleTimeMs)
                        continue;

                    // Claim 消息（转移到当前消费者）
                    var claimed = await db.StreamClaimAsync(
                        _streamKey,
                        _consumerGroup,
                        _consumerId,
                        minIdleTimeInMs: _options.MinIdleTimeMs,
                        messageIds: new[] { pendingMessage.MessageId });

                    if (claimed.Length > 0)
                    {
                        _logger.LogWarning("Retrying pending message {MessageId} (idle: {IdleMs}ms, deliveries: {DeliveryCount})",
                            pendingMessage.MessageId, pendingMessage.IdleTimeInMilliseconds, pendingMessage.DeliveryCount);

                        await ProcessMessageAsync(db, claimed[0], handler, cancellationToken, isRetry: true);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error processing pending messages");
                await Task.Delay(5000, cancellationToken);
            }
        }
    }

    private async Task EnsureConsumerGroupExistsAsync(IDatabase db)
    {
        try
        {
            await db.StreamCreateConsumerGroupAsync(_streamKey, _consumerGroup, StreamPosition.NewMessages);
            _logger.LogInformation("Created Consumer Group: {Group} for Stream: {Stream}", _consumerGroup, _streamKey);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            _logger.LogDebug("Consumer Group {Group} already exists", _consumerGroup);
        }
    }

    [RequiresUnreferencedCode("JSON deserialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("JSON deserialization may require runtime code generation")]
    private async Task ProcessMessageAsync<TMessage>(IDatabase db, StreamEntry streamEntry, Func<TMessage, TransportContext, Task> handler, CancellationToken cancellationToken, bool isRetry = false) where TMessage : class
    {
        var (payload, messageId, qos, retryCount) = ExtractMessageFields(streamEntry);

        try
        {
            if (string.IsNullOrEmpty(payload))
            {
                _logger.LogWarning("Message {MessageId} has no payload", streamEntry.Id);
                await db.StreamAcknowledgeAsync(_streamKey, _consumerGroup, streamEntry.Id);
                return;
            }

            var message = JsonSerializer.Deserialize<TMessage>(payload);
            if (message == null)
            {
                _logger.LogWarning("Failed to deserialize message {MessageId}", streamEntry.Id);
                await db.StreamAcknowledgeAsync(_streamKey, _consumerGroup, streamEntry.Id);
                return;
            }

            await handler(message, new TransportContext { MessageId = messageId, RetryCount = retryCount });

            // QoS 1/2 需要 ACK
            if (qos != QualityOfService.AtMostOnce)
            {
                await db.StreamAcknowledgeAsync(_streamKey, _consumerGroup, streamEntry.Id);
                _logger.LogDebug("Processed and ACKed message {MessageId} (QoS={QoS}, Retry={RetryCount})",
                    streamEntry.Id, qos, retryCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process message {MessageId}", streamEntry.Id);

            if (retryCount >= _options.MaxRetries)
            {
                _logger.LogError("Message {MessageId} exceeded max retries ({MaxRetries}), moving to DLQ",
                    streamEntry.Id, _options.MaxRetries);
                await MoveToDLQAsync(db, streamEntry, ex);
                await db.StreamAcknowledgeAsync(_streamKey, _consumerGroup, streamEntry.Id);
            }
            else if (qos == QualityOfService.AtMostOnce)
            {
                // QoS 0: 失败后直接ACK丢弃，不重试
                _logger.LogWarning("Message {MessageId} failed with QoS=0, discarding", streamEntry.Id);
                await db.StreamAcknowledgeAsync(_streamKey, _consumerGroup, streamEntry.Id);
            }
            // QoS 1/2: 不 ACK，留在 Pending List 等待重试
        }
    }

    private (string Payload, string MessageId, QualityOfService QoS, int RetryCount) ExtractMessageFields(StreamEntry entry)
    {
        string GetField(string name) => entry.Values.FirstOrDefault(v => v.Name == name).Value.ToString();
        int GetInt(string name, int defaultValue = 0) =>
            int.TryParse(GetField(name), out var val) ? val : defaultValue;

        return (
            GetField("payload"),
            GetField("messageId") ?? entry.Id.ToString(),
            (QualityOfService)GetInt("qos"),
            GetInt("retryCount")
        );
    }

    /// <summary>
    /// 移动消息到 Dead Letter Queue
    /// </summary>
    private async Task MoveToDLQAsync(IDatabase db, StreamEntry streamEntry, Exception ex)
    {
        try
        {
            var dlqKey = $"{_streamKey}:dlq";
            var dlqFields = streamEntry.Values.ToList();
            dlqFields.Add(new NameValueEntry("error", ex.Message));
            dlqFields.Add(new NameValueEntry("errorType", ex.GetType().Name));
            dlqFields.Add(new NameValueEntry("failedAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()));

            await db.StreamAddAsync(dlqKey, dlqFields.ToArray());
            _logger.LogInformation("Moved message {MessageId} to DLQ", streamEntry.Id);
        }
        catch (Exception dlqEx)
        {
            _logger.LogError(dlqEx, "Failed to move message {MessageId} to DLQ", streamEntry.Id);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        await Task.CompletedTask;
    }
}
