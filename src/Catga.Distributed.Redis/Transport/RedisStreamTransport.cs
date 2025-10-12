using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Catga.Transport;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catga.Distributed.Redis;

/// <summary>Redis Streams transport (native Consumer Groups, ACK, Pending List)</summary>
public sealed class RedisStreamTransport : IMessageTransport, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisStreamTransport> _logger;
    private readonly string _streamKey;
    private readonly string _consumerGroup;
    private readonly string _consumerId;
    private readonly CancellationTokenSource _disposeCts;

    public RedisStreamTransport(IConnectionMultiplexer redis, ILogger<RedisStreamTransport> logger, string streamKey = "catga:messages", string consumerGroup = "catga-group", string? consumerId = null)
    {
        _redis = redis;
        _logger = logger;
        _streamKey = streamKey;
        _consumerGroup = consumerGroup;
        _consumerId = consumerId ?? $"consumer-{Guid.NewGuid():N}";
        _disposeCts = new CancellationTokenSource();
    }

    public string Name => "Redis Streams";
    public BatchTransportOptions? BatchOptions => new() { MaxBatchSize = 100, BatchTimeout = TimeSpan.FromMilliseconds(100), EnableAutoBatching = true };
    public CompressionTransportOptions? CompressionOptions => null;

    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
    {
        var db = _redis.GetDatabase();
        var payload = JsonSerializer.Serialize(message);
        var fields = new NameValueEntry[]
        {
            new("type", typeof(TMessage).FullName!),
            new("payload", payload),
            new("messageId", context?.MessageId ?? Guid.NewGuid().ToString()),
            new("timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString())
        };
        await db.StreamAddAsync(_streamKey, fields);
        _logger.LogDebug("Published message {MessageId} to Redis Stream {Stream}", context?.MessageId ?? "unknown", _streamKey);
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
        _logger.LogInformation("Starting Redis Stream consumer: {ConsumerId} in group: {Group}", _consumerId, _consumerGroup);
        while (!cancellationToken.IsCancellationRequested && !_disposeCts.Token.IsCancellationRequested)
        {
            try
            {
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

    private async Task ProcessMessageAsync<TMessage>(IDatabase db, StreamEntry streamEntry, Func<TMessage, TransportContext, Task> handler, CancellationToken cancellationToken) where TMessage : class
    {
        try
        {
            var payloadValue = streamEntry.Values.FirstOrDefault(v => v.Name == "payload").Value;
            var messageIdValue = streamEntry.Values.FirstOrDefault(v => v.Name == "messageId").Value;
            if (!payloadValue.HasValue)
            {
                _logger.LogWarning("Message {MessageId} has no payload", streamEntry.Id);
                return;
            }
            var message = JsonSerializer.Deserialize<TMessage>(payloadValue.ToString());
            if (message == null)
            {
                _logger.LogWarning("Failed to deserialize message {MessageId}", streamEntry.Id);
                return;
            }
            var context = new TransportContext { MessageId = messageIdValue.HasValue ? messageIdValue.ToString() : streamEntry.Id.ToString() };
            await handler(message, context);
            await db.StreamAcknowledgeAsync(_streamKey, _consumerGroup, streamEntry.Id);
            _logger.LogDebug("Processed and ACKed message {MessageId}", streamEntry.Id);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to process message {MessageId}", streamEntry.Id); }
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        await Task.CompletedTask;
    }
}
