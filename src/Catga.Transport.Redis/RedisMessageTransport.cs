using System;
using Catga.Abstractions;
using Catga.Core;
using Catga.Transport;
using StackExchange.Redis;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Catga.Transport;

/// <summary>
/// Redis-based message transport with QoS support:
/// - QoS 0 (AtMostOnce): Uses Redis Pub/Sub (fast, no persistence)
/// - QoS 1 (AtLeastOnce): Uses Redis Streams (persistent, acknowledged)
/// </summary>
public sealed class RedisMessageTransport : IMessageTransport, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ISubscriber _subscriber;
    private readonly IMessageSerializer _serializer;
    private readonly string _consumerGroup;
    private readonly string _consumerName;
    private readonly string _channelPrefix = "catga."; // default logical prefix
    private readonly Func<Type, string>? _naming;       // optional channel naming

    // Pub/Sub subscriptions (QoS 0)
    private readonly ConcurrentDictionary<string, ChannelMessageQueue> _pubSubSubscriptions = new();

    // Stream subscriptions (QoS 1)
    private readonly ConcurrentDictionary<string, Task> _streamTasks = new();
    private readonly CancellationTokenSource _cts = new();

    public string Name => "Redis";
    public BatchTransportOptions? BatchOptions => null;
    public CompressionTransportOptions? CompressionOptions => null;

    public RedisMessageTransport(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        string? consumerGroup = null,
        string? consumerName = null)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _db = _redis.GetDatabase();
        _subscriber = _redis.GetSubscriber();
        _consumerGroup = consumerGroup ?? $"catga-group-{Environment.MachineName}";
        _consumerName = consumerName ?? $"catga-consumer-{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Overload that accepts RedisTransportOptions to enable naming convention and channel prefix.
    /// </summary>
    public RedisMessageTransport(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        RedisTransportOptions options,
        string? consumerGroup = null,
        string? consumerName = null)
        : this(redis, serializer, consumerGroup, consumerName)
    {
        if (options != null)
        {
            // Ensure trailing dot for simple concatenation
            var prefix = options.ChannelPrefix ?? "catga.";
            _channelPrefix = prefix.EndsWith('.') ? prefix : prefix + ".";
            _naming = options.Naming;
        }
    }

    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(message);

        var subject = GetSubject<TMessage>();
        var payload = SerializeMessage(message, context);

        // Always use Pub/Sub for broadcast messages
        await _subscriber.PublishAsync(
            RedisChannel.Literal(subject),
            payload,
            CommandFlags.FireAndForget);
    }

    public async Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        string destination,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(message);

        var payload = SerializeMessage(message, context);
        var streamKey = $"stream:{destination}";

        // Use Streams for point-to-point messaging
        await _db.StreamAddAsync(
            streamKey,
            "data",
            payload,
            flags: CommandFlags.DemandMaster);
    }

    public async Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(handler);

        var subject = GetSubject<TMessage>();
        var queue = await _subscriber.SubscribeAsync(RedisChannel.Literal(subject));
        _pubSubSubscriptions[subject] = queue;

        queue.OnMessage(async channelMessage =>
        {
            try
            {
                var (message, ctx) = DeserializeMessage<TMessage>(channelMessage.Message!);
                await handler(message, ctx ?? new TransportContext());
            }
            catch
            {
                // Log error
            }
        });
    }

    public async Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        IEnumerable<TMessage> messages,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        await BatchOperationHelper.ExecuteBatchAsync(
            messages,
            m => PublishAsync(m, context, cancellationToken));
    }

    public async Task SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        IEnumerable<TMessage> messages,
        string destination,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        await BatchOperationHelper.ExecuteBatchAsync(
            messages,
            destination,
            (m, dest) => SendAsync(m, dest, context, cancellationToken));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string GetSubject<TMessage>() where TMessage : class
    {
        var name = _naming != null ? _naming(typeof(TMessage)) : typeof(TMessage).Name;
        return _channelPrefix + name;
    }

    private string SerializeMessage<TMessage>(TMessage message, TransportContext? context)
        where TMessage : class
    {
        var envelope = new MessageEnvelope<TMessage>
        {
            Message = message,
            Context = context
        };
        var bytes = _serializer.Serialize(envelope, typeof(MessageEnvelope<TMessage>));
        return Convert.ToBase64String(bytes);
    }

    private (TMessage Message, TransportContext? Context) DeserializeMessage<TMessage>(RedisValue data)
        where TMessage : class
    {
        var bytes = Convert.FromBase64String(data.ToString()!);
        var envelope = (MessageEnvelope<TMessage>?)_serializer.Deserialize(bytes, typeof(MessageEnvelope<TMessage>));
        return (envelope!.Message, envelope.Context);
    }

    private sealed class MessageEnvelope<T> where T : class
    {
        public T Message { get; set; } = default!;
        public TransportContext? Context { get; set; }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();

        // Unsubscribe all Pub/Sub
        foreach (var queue in _pubSubSubscriptions.Values)
        {
            queue.Unsubscribe();
        }
        _pubSubSubscriptions.Clear();

        // Wait for stream tasks to complete
        if (_streamTasks.Count > 0)
        {
            await Task.WhenAll(_streamTasks.Values);
        }
        _streamTasks.Clear();

        _cts.Dispose();
    }
}
