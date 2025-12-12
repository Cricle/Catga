using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Abstractions;
using Catga.Core;
using Catga.Observability;
using Catga.Resilience;
using StackExchange.Redis;
using StackExchange.Redis.MultiplexerPool;

namespace Catga.Transport;

/// <summary>Redis-based message transport with QoS support.</summary>
public sealed partial class RedisMessageTransport : IMessageTransport, IAsyncDisposable
{
    private readonly IConnectionMultiplexerPool _pool;
    private readonly IMessageSerializer _ser;
    private readonly IResiliencePipelineProvider _provider;
    private readonly string _group;
    private readonly string _consumer;
    private readonly string _prefix;
    private readonly Func<Type, string>? _naming;
    private readonly RedisTransportOptions? _opts;
    private readonly ConcurrentDictionary<string, ChannelMessageQueue> _pubSubs = new();
    private readonly ConcurrentDictionary<string, Task> _streams = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentQueue<(bool IsStream, string Dest, string Payload, string? Tp, string? Ts)> _batch = new();
    private int _batchCount;
    private Timer? _timer;
    private readonly object _flushLock = new();

    public string Name => "Redis";
    public BatchTransportOptions? BatchOptions => null;
    public CompressionTransportOptions? CompressionOptions => null;

    /// <summary>
    /// Get a connection from the pool
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IConnectionMultiplexer GetConnection() => _pool.GetAsync().GetAwaiter().GetResult().Connection;

    /// <summary>
    /// Get a connection from the pool asynchronously
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask<IConnectionMultiplexer> GetConnectionAsync() => (await _pool.GetAsync()).Connection;

    /// <summary>
    /// Get a database from the pool
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IDatabase GetDatabase() => GetConnection().GetDatabase();

    /// <summary>
    /// Get a subscriber from the pool
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ISubscriber GetSubscriber() => GetConnection().GetSubscriber();

    public RedisMessageTransport(IConnectionMultiplexerPool pool, IMessageSerializer serializer, IResiliencePipelineProvider provider, RedisTransportOptions? options = null, string? consumerGroup = null, string? consumerName = null)
    {
        _pool = pool;
        _ser = serializer;
        _provider = provider;
        _group = consumerGroup ?? $"catga-group-{Environment.MachineName}";
        _consumer = consumerName ?? $"catga-consumer-{Guid.NewGuid():N}";
        _opts = options;
        var p = options?.ChannelPrefix ?? "catga.";
        _prefix = p.EndsWith('.') ? p : p + ".";
        _naming = options?.Naming;
        if (options?.Batch is { EnableAutoBatching: true } b)
            _timer = new Timer(static s => { try { ((RedisMessageTransport)s!).TryFlushBatchTimer(); } catch { } }, this, b.BatchTimeout, b.BatchTimeout);
    }

    /// <summary>
    /// Constructor for backward compatibility with single IConnectionMultiplexer
    /// </summary>
    public RedisMessageTransport(IConnectionMultiplexer redis, IMessageSerializer serializer, IResiliencePipelineProvider provider, RedisTransportOptions? options = null, string? consumerGroup = null, string? consumerName = null)
        : this(new SingleConnectionPool(redis), serializer, provider, options, consumerGroup, consumerName)
    {
    }

    /// <summary>
    /// Wrapper to adapt single IConnectionMultiplexer to IConnectionMultiplexerPool interface
    /// </summary>
    private sealed class SingleConnectionPool : IConnectionMultiplexerPool
    {
        private readonly IConnectionMultiplexer _connection;
        private readonly ReconnectableConnectionMultiplexerWrapper _wrapper;

        public SingleConnectionPool(IConnectionMultiplexer connection)
        {
            _connection = connection;
            _wrapper = new ReconnectableConnectionMultiplexerWrapper(connection);
        }

        public int PoolSize => 1;

        public Task<IReconnectableConnectionMultiplexer> GetAsync() => Task.FromResult<IReconnectableConnectionMultiplexer>(_wrapper);

        public Task CloseAllAsync(bool allowCommandsToComplete = true) => _connection.CloseAsync(allowCommandsToComplete);

        public void Dispose() => _connection.Dispose();

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }

        /// <summary>
        /// Wrapper to adapt IConnectionMultiplexer to IReconnectableConnectionMultiplexer
        /// </summary>
        private sealed class ReconnectableConnectionMultiplexerWrapper : IReconnectableConnectionMultiplexer
        {
            private readonly IConnectionMultiplexer _inner;

            public ReconnectableConnectionMultiplexerWrapper(IConnectionMultiplexer inner) => _inner = inner;

            public IConnectionMultiplexer Multiplexer => _inner;

            public IConnectionMultiplexer Connection => _inner;

            public int ConnectionIndex => 0;

            public DateTime ConnectionTimeUtc { get; } = DateTime.UtcNow;

            public Task ReconnectAsync(bool abortOnConnectFail = true, bool allowCommandsToComplete = true) => Task.CompletedTask;
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

        using var activity = ObservabilityHooks.IsEnabled ? CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Publish", ActivityKind.Producer) : null;
        if (activity != null)
        {
            activity.SetTag(CatgaActivitySource.Tags.MessagingSystem, "redis");
            activity.SetTag(CatgaActivitySource.Tags.MessagingDestination, subject);
            activity.SetTag(CatgaActivitySource.Tags.MessagingOperation, "publish");
            activity.SetTag(CatgaActivitySource.Tags.MessageType, TypeNameCache<TMessage>.Name);
        }

        // Metrics tags
        var tag_component = new KeyValuePair<string, object?>("component", "Transport.Redis");
        var tag_type = new KeyValuePair<string, object?>("message_type", TypeNameCache<TMessage>.Name);
        var tag_dest = new KeyValuePair<string, object?>("destination", subject);

        // Optional auto-batching for Pub/Sub
        if (_opts?.Batch is { EnableAutoBatching: true } batchOptions)
        {
            Enqueue(isStream: false, destination: subject, payload: payload, traceParent: null, traceState: null, batchOptions);
            return;
        }

        try
        {
            // Always use Pub/Sub for broadcast messages
            await _provider.ExecuteTransportPublishAsync(ct => new ValueTask(
                GetSubscriber().PublishAsync(
                    RedisChannel.Literal(subject),
                    payload,
                    CommandFlags.FireAndForget)),
                cancellationToken);
            if (ObservabilityHooks.IsEnabled) CatgaDiagnostics.MessagesPublished.Add(1, tag_component, tag_type, tag_dest);
            System.Diagnostics.Activity.Current?.AddActivityEvent("Messaging.Publish.Sent",
                ("channel", subject));
        }
        catch (Exception)
        {
            if (ObservabilityHooks.IsEnabled) CatgaDiagnostics.MessagesFailed.Add(1,
                new KeyValuePair<string, object?>("component", "Transport.Redis"),
                new KeyValuePair<string, object?>("destination", subject));
            System.Diagnostics.Activity.Current?.SetError(System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(new Exception()).SourceException ?? new Exception("Publish failed"));
            System.Diagnostics.Activity.Current?.AddActivityEvent("Messaging.Publish.Failed",
                ("channel", subject));
            throw;
        }
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

        using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Send", ActivityKind.Producer);
        if (activity != null)
        {
            activity.SetTag(CatgaActivitySource.Tags.MessagingSystem, "redis");
            activity.SetTag(CatgaActivitySource.Tags.MessagingDestination, streamKey);
            activity.SetTag(CatgaActivitySource.Tags.MessagingOperation, "send");
            activity.SetTag(CatgaActivitySource.Tags.MessageType, TypeNameCache<TMessage>.Name);
        }

        var tag_component = new KeyValuePair<string, object?>("component", "Transport.Redis");
        var tag_type = new KeyValuePair<string, object?>("message_type", TypeNameCache<TMessage>.Name);
        var tag_dest = new KeyValuePair<string, object?>("destination", streamKey);
        // Optional auto-batching for Streams
        string? tp = null, ts = null;
        if (ObservabilityHooks.IsEnabled)
        {
            var current = Activity.Current;
            if (current != null)
            {
                tp = current.Id;
                ts = current.TraceStateString;
            }
        }

        if (_opts?.Batch is { EnableAutoBatching: true } batchOptions)
        {
            Enqueue(isStream: true, destination: streamKey, payload: payload, traceParent: tp, traceState: ts, batchOptions);
            return;
        }

        try
        {
            // Use Streams for point-to-point messaging
            await _provider.ExecuteTransportSendAsync(ct => new ValueTask(
                GetDatabase().StreamAddAsync(
                    streamKey,
                    new NameValueEntry[] {
                        new NameValueEntry("data", payload),
                        tp is null ? default : new NameValueEntry("traceparent", tp),
                        ts is null ? default : new NameValueEntry("tracestate", ts)
                    }.Where(e => e.Name.HasValue).ToArray(),
                    flags: CommandFlags.DemandMaster)),
                cancellationToken);
            if (ObservabilityHooks.IsEnabled) CatgaDiagnostics.MessagesPublished.Add(1, tag_component, tag_type, tag_dest);
            System.Diagnostics.Activity.Current?.AddActivityEvent("Messaging.Stream.Added",
                ("stream", streamKey));
        }
        catch (Exception)
        {
            if (ObservabilityHooks.IsEnabled) CatgaDiagnostics.MessagesFailed.Add(1,
                new KeyValuePair<string, object?>("component", "Transport.Redis"),
                new KeyValuePair<string, object?>("destination", streamKey));
            System.Diagnostics.Activity.Current?.SetError(System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(new Exception()).SourceException ?? new Exception("Stream add failed"));
            System.Diagnostics.Activity.Current?.AddActivityEvent("Messaging.Stream.Failed",
                ("stream", streamKey));
            throw;
        }
    }

    public async Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(handler);

        var subject = GetSubject<TMessage>();
        var queue = await GetSubscriber().SubscribeAsync(RedisChannel.Literal(subject));
        _pubSubs[subject] = queue;

        queue.OnMessage(async channelMessage =>
        {
            using var activity = ObservabilityHooks.IsEnabled ? CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Receive", ActivityKind.Consumer) : null;
            if (activity != null)
            {
                activity.SetTag(CatgaActivitySource.Tags.MessagingSystem, "redis");
                activity.SetTag(CatgaActivitySource.Tags.MessagingDestination, subject);
                activity.SetTag(CatgaActivitySource.Tags.MessagingOperation, "receive");
                activity.SetTag(CatgaActivitySource.Tags.MessageType, TypeNameCache<TMessage>.Name);
            }
            try
            {
                var deserStart = Stopwatch.GetTimestamp();
                var (message, ctx) = DeserializeMessage<TMessage>(channelMessage.Message!);
                var deserMs = (Stopwatch.GetTimestamp() - deserStart) * 1000.0 / Stopwatch.Frequency;
                activity?.AddActivityEvent("Messaging.Receive.Deserialized",
                    ("message.type", TypeNameCache<TMessage>.Name),
                    ("duration.ms", deserMs));
                var handlerStart = Stopwatch.GetTimestamp();
                await handler(message, ctx ?? new TransportContext());
                var handlerMs = (Stopwatch.GetTimestamp() - handlerStart) * 1000.0 / Stopwatch.Frequency;
                activity?.AddActivityEvent("Messaging.Receive.Handler",
                    ("channel", subject),
                    ("duration.ms", handlerMs));
                activity?.AddActivityEvent("Messaging.Receive.Processed",
                    ("channel", subject));
            }
            catch (Exception ex)
            {
                activity?.SetError(ex);
                // Log error and count failure
                if (ObservabilityHooks.IsEnabled) CatgaDiagnostics.MessagesFailed.Add(1,
                    new KeyValuePair<string, object?>("component", "Transport.Redis"),
                    new KeyValuePair<string, object?>("destination", subject));
            }
        });

        // Start Redis Streams (QoS1) consumer for this subject
        var streamKey = $"stream:{subject}";
        if (!_streams.ContainsKey(streamKey))
        {
            var task = Task.Run(async () =>
            {
                try
                {
                    // Ensure consumer group exists (create stream if missing)
                    try
                    {
                        await GetDatabase().StreamCreateConsumerGroupAsync(streamKey, _group, StreamPosition.NewMessages, createStream: true);
                    }
                    catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase))
                    {
                        // group already exists
                    }

                    while (!_cts.IsCancellationRequested)
                    {
                        var entries = await GetDatabase().StreamReadGroupAsync(streamKey, _group, _consumer, ">", count: 1);
                        if (entries is null || entries.Length == 0)
                        {
                            try { await Task.Delay(200, _cts.Token); } catch { }
                            continue;
                        }

                        foreach (var entry in entries)
                        {
                            Activity? activity2 = null;
                            string? tp = null, ts = null, dataStr = null;
                            foreach (var nv in entry.Values)
                            {
                                if (nv.Name == "traceparent") tp = nv.Value;
                                else if (nv.Name == "tracestate") ts = nv.Value;
                                else if (nv.Name == "data") dataStr = nv.Value;
                            }

                            if (ObservabilityHooks.IsEnabled && !string.IsNullOrEmpty(tp))
                            {
                                try
                                {
                                    var parent = ActivityContext.Parse(tp!, ts);
                                    activity2 = CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Receive", ActivityKind.Consumer, parent);
                                }
                                catch
                                {
                                    activity2 = CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Receive", ActivityKind.Consumer);
                                }
                            }
                            else
                            {
                                activity2 = ObservabilityHooks.IsEnabled ? CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Receive", ActivityKind.Consumer) : null;
                            }

                            if (activity2 != null)
                            {
                                activity2.SetTag(CatgaActivitySource.Tags.MessagingSystem, "redis");
                                activity2.SetTag(CatgaActivitySource.Tags.MessagingDestination, streamKey);
                                activity2.SetTag(CatgaActivitySource.Tags.MessagingOperation, "receive");
                                activity2.SetTag(CatgaActivitySource.Tags.MessageType, TypeNameCache<TMessage>.Name);
                            }

                            try
                            {
                                var deserStart2 = Stopwatch.GetTimestamp();
                                var (message2, _) = DeserializeMessage<TMessage>(dataStr!);
                                var deserMs2 = (Stopwatch.GetTimestamp() - deserStart2) * 1000.0 / Stopwatch.Frequency;
                                int payloadSize = 0; try { payloadSize = Convert.FromBase64String(dataStr!).Length; } catch { }
                                activity2?.AddActivityEvent("Messaging.Receive.Deserialized",
                                    ("message.type", TypeNameCache<TMessage>.Name),
                                    ("duration.ms", deserMs2),
                                    ("payload.size", payloadSize));

                                var handlerStart2 = Stopwatch.GetTimestamp();
                                await handler(message2, new TransportContext());
                                var handlerMs2 = (Stopwatch.GetTimestamp() - handlerStart2) * 1000.0 / Stopwatch.Frequency;
                                activity2?.AddActivityEvent("Messaging.Receive.Handler",
                                    ("stream", streamKey),
                                    ("duration.ms", handlerMs2));
                                activity2?.AddActivityEvent("Messaging.Receive.Processed",
                                    ("stream", streamKey));

                                await GetDatabase().StreamAcknowledgeAsync(streamKey, _group, entry.Id);
                            }
                            catch (Exception ex)
                            {
                                activity2?.SetError(ex);
                                if (ObservabilityHooks.IsEnabled)
                                {
                                    CatgaDiagnostics.MessagesFailed.Add(1,
                                        new KeyValuePair<string, object?>("component", "Transport.Redis"),
                                        new KeyValuePair<string, object?>("destination", streamKey));
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // swallow cancellation/unexpected loop errors
                }
            }, _cts.Token);
            _streams[streamKey] = task;
        }
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
        return _prefix + name;
    }

    private string SerializeMessage<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message, TransportContext? context)
        where TMessage : class
    {
        var bytes = _ser.Serialize(message, typeof(TMessage));
        return Convert.ToBase64String(bytes);
    }

    private (TMessage Message, TransportContext? Context) DeserializeMessage<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(RedisValue data)
        where TMessage : class
    {
        var bytes = Convert.FromBase64String(data.ToString()!);
        var message = (TMessage?)_ser.Deserialize(bytes, typeof(TMessage));
        return (message!, null);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();

        // Unsubscribe all Pub/Sub
        foreach (var queue in _pubSubs.Values)
        {
            queue.Unsubscribe();
        }
        _pubSubs.Clear();

        // Wait for stream tasks to complete
        if (_streams.Count > 0)
        {
            await Task.WhenAll(_streams.Values);
        }
        _streams.Clear();

        _timer?.Dispose();
        _cts.Dispose();
    }

    private void Enqueue(bool isStream, string destination, string payload, string? traceParent, string? traceState, BatchTransportOptions batch)
    {
        var newCount = Interlocked.Increment(ref _batchCount);
        if (_opts!.MaxQueueLength > 0 && newCount > _opts.MaxQueueLength)
        {
            while (Interlocked.CompareExchange(ref _batchCount, _batchCount, _batchCount) > _opts.MaxQueueLength && _batch.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _batchCount);
            }
        }
        _batch.Enqueue((isStream, destination, payload, traceParent, traceState));
        if (!isStream)
            System.Diagnostics.Activity.Current?.AddActivityEvent("Messaging.Publish.Enqueued",
                ("channel", destination));
        else
            System.Diagnostics.Activity.Current?.AddActivityEvent("Messaging.Stream.Enqueued",
                ("stream", destination));
        if (newCount >= batch.MaxBatchSize)
            TryFlushBatchImmediate(batch);
    }

    private void TryFlushBatchImmediate(BatchTransportOptions batch)
    {
        if (!Monitor.TryEnter(_flushLock)) return;
        try { FlushInternal(batch).GetAwaiter().GetResult(); }
        finally { Monitor.Exit(_flushLock); }
    }

    private void TryFlushBatchTimer()
    {
        var batch = _opts?.Batch;
        if (batch is null || !batch.EnableAutoBatching) return;
        if (!Monitor.TryEnter(_flushLock)) return;
        try { FlushInternal(batch).GetAwaiter().GetResult(); }
        finally { Monitor.Exit(_flushLock); }
    }

    private async Task FlushInternal(BatchTransportOptions batch)
    {
        int toProcess = Math.Min(_batchCount, batch.MaxBatchSize);
        if (toProcess <= 0) return;
        var list = new List<(bool IsStream, string Destination, string Payload, string? TraceParent, string? TraceState)>(toProcess);
        while (toProcess-- > 0 && _batch.TryDequeue(out var item))
        {
            Interlocked.Decrement(ref _batchCount);
            list.Add(item);
        }
        using var batchSpan = ObservabilityHooks.IsEnabled ? CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Batch.Flush", ActivityKind.Producer) : null;
        foreach (var item in list)
        {
            try
            {
                if (!item.IsStream)
                {
                    await _provider.ExecuteTransportPublishAsync(ct => new ValueTask(
                        GetSubscriber().PublishAsync(RedisChannel.Literal(item.Destination), item.Payload, CommandFlags.FireAndForget)), _cts.Token);
                    batchSpan?.AddActivityEvent("Batch.PubSub.Sent",
                        ("channel", item.Destination));
                }
                else
                {
                    var entries = new List<NameValueEntry>(4) { new NameValueEntry("data", item.Payload) };
                    if (!string.IsNullOrEmpty(item.TraceParent)) entries.Add(new NameValueEntry("traceparent", item.TraceParent));
                    if (!string.IsNullOrEmpty(item.TraceState)) entries.Add(new NameValueEntry("tracestate", item.TraceState));
                    await _provider.ExecuteTransportSendAsync(ct => new ValueTask(
                        GetDatabase().StreamAddAsync(item.Destination, entries.ToArray(), flags: CommandFlags.DemandMaster)), _cts.Token);
                    batchSpan?.AddActivityEvent("Batch.Stream.Added",
                        ("stream", item.Destination));
                }
            }
            catch (Exception ex)
            {
                // count failure only if tracing enabled
                if (ObservabilityHooks.IsEnabled)
                {
                    CatgaDiagnostics.MessagesFailed.Add(1,
                        new KeyValuePair<string, object?>("component", "Transport.Redis"),
                        new KeyValuePair<string, object?>("destination", item.Destination),
                        new KeyValuePair<string, object?>("reason", "batch_item"));
                }
                batchSpan?.SetError(ex);
                batchSpan?.AddActivityEvent("Batch.Item.Failed",
                    ("destination", item.Destination));
            }
        }
    }
}
