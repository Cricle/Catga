using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Abstractions;
using Catga.Core;
using Catga.Hosting;
using Catga.Observability;
using Catga.Resilience;
using Catga.Transport.Redis.Observability;
using StackExchange.Redis;
using StackExchange.Redis.MultiplexerPool;

namespace Catga.Transport;

/// <summary>Redis-based message transport with QoS support.</summary>
public sealed class RedisMessageTransport : MessageTransportBase, IAsyncInitializable, IStoppable, IWaitable, IHealthCheckable, IAsyncDisposable
{
    private readonly IConnectionMultiplexerPool _pool;
    private readonly RedisTransportOptions? _opts;
    private readonly string _group;
    private readonly string _consumer;
    private readonly ConcurrentDictionary<string, ChannelMessageQueue> _pubSubs = new();
    private readonly ConcurrentDictionary<string, Task> _streams = new();
    private readonly ConcurrentDictionary<long, byte> _processedMessages = new(); // QoS2 deduplication cache

    // IStoppable implementation
    private volatile bool _acceptingMessages = true;
    
    // IWaitable implementation
    private int _pendingOperations = 0;
    
    // IHealthCheckable implementation
    private volatile bool _isHealthy = false;
    private DateTimeOffset? _lastHealthCheck;

    public override string Name => "Redis";
    
    // IStoppable properties
    public bool IsAcceptingMessages => _acceptingMessages;
    
    // IWaitable properties
    public int PendingOperations => _pendingOperations;
    
    // IHealthCheckable properties
    public bool IsHealthy => _isHealthy;
    public string? HealthStatus => _isHealthy ? "Connected" : "Disconnected";
    public DateTimeOffset? LastHealthCheck => _lastHealthCheck;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IConnectionMultiplexer GetConnection() => _pool.GetAsync().GetAwaiter().GetResult().Connection;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask<IConnectionMultiplexer> GetConnectionAsync() => (await _pool.GetAsync()).Connection;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IDatabase GetDatabase() => GetConnection().GetDatabase();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ISubscriber GetSubscriber() => GetConnection().GetSubscriber();

    public RedisMessageTransport(
        IConnectionMultiplexerPool pool,
        IMessageSerializer serializer,
        IResiliencePipelineProvider provider,
        RedisTransportOptions? options = null,
        string? consumerGroup = null,
        string? consumerName = null)
        : base(serializer, provider, options?.ChannelPrefix ?? "catga.", options?.Naming)
    {
        _pool = pool;
        _opts = options;
        _group = consumerGroup ?? $"catga-group-{Environment.MachineName}";
        _consumer = consumerName ?? $"catga-consumer-{Guid.NewGuid():N}";
        InitializeBatchTimer(options?.Batch);
    }

    // IAsyncInitializable implementation
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Test connection by pinging Redis
            var connection = await GetConnectionAsync();
            var db = connection.GetDatabase();
            await db.PingAsync();
            
            _isHealthy = true;
            _lastHealthCheck = DateTimeOffset.UtcNow;
        }
        catch (Exception)
        {
            _isHealthy = false;
            _lastHealthCheck = DateTimeOffset.UtcNow;
            throw;
        }
    }

    // IStoppable implementation
    public void StopAcceptingMessages()
    {
        _acceptingMessages = false;
    }

    // IWaitable implementation
    public async Task WaitForCompletionAsync(CancellationToken cancellationToken = default)
    {
        var timeout = TimeSpan.FromSeconds(30);
        var sw = Stopwatch.StartNew();
        
        while (_pendingOperations > 0 && sw.Elapsed < timeout)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
            
            try
            {
                await Task.Delay(100, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelled
                break;
            }
        }
    }

    public RedisMessageTransport(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        IResiliencePipelineProvider provider,
        RedisTransportOptions? options = null,
        string? consumerGroup = null,
        string? consumerName = null)
        : this(new SingleConnectionPool(redis), serializer, provider, options, consumerGroup, consumerName)
    {
    }

    public override async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Check if accepting messages
        if (!_acceptingMessages)
        {
            throw new InvalidOperationException("Transport is not accepting new messages");
        }
        
        Interlocked.Increment(ref _pendingOperations);
        try
        {
            var subject = GetSubject<TMessage>();
            var payload = SerializeToBase64(message);

            using var activity = StartPublishActivity("redis", subject, TypeNameCache<TMessage>.Name);

            // QoS2 deduplication check
            var msg = message as IMessage;
            var qos = msg?.QoS ?? QualityOfService.AtLeastOnce;
            if (qos == QualityOfService.ExactlyOnce && context?.MessageId.HasValue == true)
            {
                var dedupKey = $"dedup:{context.Value.MessageId}";
                var db = GetDatabase();
                var wasSet = await db.StringSetAsync(dedupKey, "1", TimeSpan.FromMinutes(5), When.NotExists);
                if (!wasSet)
                {
                    activity?.SetTag("catga.idempotent", true);
                    return; // Already processed
                }
            }

            if (_opts?.Batch is { EnableAutoBatching: true } batchOptions)
            {
                EnqueueBatch(new BatchItem(subject, [], null, null, (false, payload)), batchOptions, _opts.MaxQueueLength);
                return;
            }

            try
            {
                await ResilienceProvider.ExecuteTransportPublishAsync(
                    _ => new ValueTask(GetSubscriber().PublishAsync(RedisChannel.Literal(subject), payload, CommandFlags.FireAndForget)),
                    cancellationToken);
                RecordPublishSuccess(TypeNameCache<TMessage>.Name, subject);
                Activity.Current?.AddActivityEvent(RedisActivityEvents.RedisPublishSent, ("channel", subject));
            }
            catch (Exception ex)
            {
                RecordPublishFailure(subject);
                Activity.Current?.SetError(ex);
                Activity.Current?.AddActivityEvent(RedisActivityEvents.RedisPublishFailed, ("channel", subject));
                throw;
            }
        }
        finally
        {
            Interlocked.Decrement(ref _pendingOperations);
        }
    }

    public override Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        string destination,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        => PublishAsync(message, context, cancellationToken);

    public override async Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var subject = GetSubject<TMessage>();
        var queue = await GetSubscriber().SubscribeAsync(RedisChannel.Literal(subject));
        _pubSubs[subject] = queue;

        queue.OnMessage(async channelMessage =>
        {
            using var activity = StartReceiveActivity("redis", subject, TypeNameCache<TMessage>.Name);
            try
            {
                var deserStart = Stopwatch.GetTimestamp();
                var message = DeserializeFromBase64<TMessage>(channelMessage.Message!);
                var deserMs = (Stopwatch.GetTimestamp() - deserStart) * 1000.0 / Stopwatch.Frequency;
                activity?.AddActivityEvent(RedisActivityEvents.RedisReceiveDeserialized,
                    ("message.type", TypeNameCache<TMessage>.Name), ("duration.ms", deserMs));

                // QoS2 deduplication check
                var msg = message as IMessage;
                var qos = msg?.QoS ?? QualityOfService.AtLeastOnce;
                if (qos == QualityOfService.ExactlyOnce && msg != null && msg.MessageId != 0)
                {
                    if (!_processedMessages.TryAdd(msg.MessageId, 0))
                    {
                        activity?.SetTag("catga.idempotent", true);
                        return; // Already processed
                    }
                }

                var handlerStart = Stopwatch.GetTimestamp();
                await handler(message, new TransportContext());
                var handlerMs = (Stopwatch.GetTimestamp() - handlerStart) * 1000.0 / Stopwatch.Frequency;
                activity?.AddActivityEvent(RedisActivityEvents.RedisReceiveHandler, ("channel", subject), ("duration.ms", handlerMs));
                activity?.AddActivityEvent(RedisActivityEvents.RedisReceiveProcessed, ("channel", subject));
            }
            catch (Exception ex)
            {
                activity?.SetError(ex);
                RecordPublishFailure(subject);
            }
        });

        // Start Redis Streams consumer
        var streamKey = $"stream:{subject}";
        if (!_streams.ContainsKey(streamKey))
        {
            var task = StartStreamConsumerAsync<TMessage>(streamKey, handler);
            _streams[streamKey] = task;
        }
    }

    private Task StartStreamConsumerAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(string streamKey, Func<TMessage, TransportContext, Task> handler) where TMessage : class
    {
        return Task.Run(async () =>
        {
            try
            {
                await EnsureConsumerGroupAsync(streamKey);

                while (!Cts.IsCancellationRequested)
                {
                    var entries = await GetDatabase().StreamReadGroupAsync(streamKey, _group, _consumer, ">", count: 1);
                    if (entries is null || entries.Length == 0)
                    {
                        try { await Task.Delay(200, Cts.Token); } catch { }
                        continue;
                    }

                    foreach (var entry in entries)
                    {
                        await ProcessStreamEntryAsync(entry, streamKey, handler);
                    }
                }
            }
            catch { /* swallow cancellation/unexpected loop errors */ }
        }, Cts.Token);
    }

    private async Task ProcessStreamEntryAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(StreamEntry entry, string streamKey, Func<TMessage, TransportContext, Task> handler) where TMessage : class
    {
        string? tp = null, ts = null, dataStr = null;
        foreach (var nv in entry.Values)
        {
            if (nv.Name == "traceparent") tp = nv.Value;
            else if (nv.Name == "tracestate") ts = nv.Value;
            else if (nv.Name == "data") dataStr = nv.Value;
        }

        using var activity = StartReceiveActivity("redis", streamKey, TypeNameCache<TMessage>.Name, tp, ts);

        try
        {
            var deserStart = Stopwatch.GetTimestamp();
            var message = DeserializeFromBase64<TMessage>(dataStr!);
            var deserMs = (Stopwatch.GetTimestamp() - deserStart) * 1000.0 / Stopwatch.Frequency;
            var payloadSize = 0;
            try { payloadSize = Convert.FromBase64String(dataStr!).Length; } catch { }
            activity?.AddActivityEvent(RedisActivityEvents.RedisReceiveDeserialized,
                ("message.type", TypeNameCache<TMessage>.Name), ("duration.ms", deserMs), ("payload.size", payloadSize));

            var handlerStart = Stopwatch.GetTimestamp();
            await handler(message, new TransportContext());
            var handlerMs = (Stopwatch.GetTimestamp() - handlerStart) * 1000.0 / Stopwatch.Frequency;
            activity?.AddActivityEvent(RedisActivityEvents.RedisReceiveHandler, ("stream", streamKey), ("duration.ms", handlerMs));
            activity?.AddActivityEvent(RedisActivityEvents.RedisReceiveProcessed, ("stream", streamKey));

            await GetDatabase().StreamAcknowledgeAsync(streamKey, _group, entry.Id);
        }
        catch (Exception ex)
        {
            activity?.SetError(ex);
            RecordPublishFailure(streamKey);
        }
    }

    protected override async Task ProcessBatchItemsAsync(List<BatchItem> items, Activity? batchSpan)
    {
        foreach (var item in items)
        {
            var (isStream, payload) = item.Extra is (bool s, string p) ? (s, p) : (false, string.Empty);
            try
            {
                if (!isStream)
                {
                    await ResilienceProvider.ExecuteTransportPublishAsync(
                        _ => new ValueTask(GetSubscriber().PublishAsync(RedisChannel.Literal(item.Destination), payload, CommandFlags.FireAndForget)),
                        Cts.Token);
                    batchSpan?.AddActivityEvent(RedisActivityEvents.RedisBatchPubSubSent, ("channel", item.Destination));
                }
                else
                {
                    var entries = BuildStreamEntries(payload, item.TraceParent, item.TraceState);
                    await ResilienceProvider.ExecuteTransportSendAsync(
                        _ => new ValueTask(GetDatabase().StreamAddAsync(item.Destination, entries, flags: CommandFlags.DemandMaster)),
                        Cts.Token);
                    batchSpan?.AddActivityEvent(RedisActivityEvents.RedisBatchStreamAdded, ("stream", item.Destination));
                }
            }
            catch (Exception ex)
            {
                RecordPublishFailure(item.Destination, "batch_item");
                batchSpan?.SetError(ex);
                batchSpan?.AddActivityEvent(RedisActivityEvents.RedisBatchItemFailed, ("destination", item.Destination));
            }
        }
    }

    private async Task EnsureConsumerGroupAsync(string streamKey)
    {
        try
        {
            await GetDatabase().StreamCreateConsumerGroupAsync(streamKey, _group, StreamPosition.NewMessages, createStream: true);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase))
        {
            // group already exists
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string SerializeToBase64<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message) where TMessage : class
    {
        var bytes = Serializer.Serialize(message, typeof(TMessage));
        return Convert.ToBase64String(bytes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TMessage DeserializeFromBase64<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(string data) where TMessage : class
    {
        var bytes = Convert.FromBase64String(data);
        return (TMessage)Serializer.Deserialize(bytes, typeof(TMessage))!;
    }

    private static NameValueEntry[] BuildStreamEntries(string payload, string? tp, string? ts)
    {
        if (string.IsNullOrEmpty(tp) && string.IsNullOrEmpty(ts))
            return [new("data", payload)];
        if (string.IsNullOrEmpty(ts))
            return [new("data", payload), new("traceparent", tp!)];
        if (string.IsNullOrEmpty(tp))
            return [new("data", payload), new("tracestate", ts!)];
        return [new("data", payload), new("traceparent", tp!), new("tracestate", ts!)];
    }

    public async ValueTask DisposeAsync()
    {
        StopAcceptingMessages();
        
        // Wait for pending operations
        try
        {
            await WaitForCompletionAsync(Cts.Token);
        }
        catch { }
        
        await DisposeAsyncCore();

        foreach (var queue in _pubSubs.Values)
            queue.Unsubscribe();
        _pubSubs.Clear();

        if (_streams.Count > 0)
            await Task.WhenAll(_streams.Values);
        _streams.Clear();
        
        _isHealthy = false;
    }

    /// <summary>Wrapper to adapt single IConnectionMultiplexer to IConnectionMultiplexerPool interface</summary>
    private sealed class SingleConnectionPool(IConnectionMultiplexer connection) : IConnectionMultiplexerPool
    {
        private readonly ReconnectableWrapper _wrapper = new(connection);

        public int PoolSize => 1;
        public Task<IReconnectableConnectionMultiplexer> GetAsync() => Task.FromResult<IReconnectableConnectionMultiplexer>(_wrapper);
        public Task CloseAllAsync(bool allowCommandsToComplete = true) => connection.CloseAsync(allowCommandsToComplete);
        public void Dispose() => connection.Dispose();
        public ValueTask DisposeAsync() { Dispose(); return default; }

        private sealed class ReconnectableWrapper(IConnectionMultiplexer inner) : IReconnectableConnectionMultiplexer
        {
            public IConnectionMultiplexer Multiplexer => inner;
            public IConnectionMultiplexer Connection => inner;
            public int ConnectionIndex => 0;
            public DateTime ConnectionTimeUtc { get; } = DateTime.UtcNow;
            public Task ReconnectAsync(bool abortOnConnectFail = true, bool allowCommandsToComplete = true) => Task.CompletedTask;
        }
    }
}
