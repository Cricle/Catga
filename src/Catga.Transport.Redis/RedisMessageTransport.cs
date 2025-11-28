using Catga.Abstractions;
using Catga.Core;
using Catga.Observability;
using Catga.Resilience;
using StackExchange.Redis;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Catga.Transport;

/// <summary>
/// Redis-based message transport with QoS support:
/// - QoS 0 (AtMostOnce): Uses Redis Pub/Sub (fast, no persistence)
/// - QoS 1 (AtLeastOnce): Uses Redis Streams (persistent, acknowledged)
/// </summary>
public sealed partial class RedisMessageTransport : IMessageTransport, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ISubscriber _subscriber;
    private readonly IMessageSerializer _serializer;
    private readonly IResiliencePipelineProvider _provider;
    private readonly string _consumerGroup;
    private readonly string _consumerName;
    private readonly string _channelPrefix = "catga."; // default logical prefix
    private readonly Func<Type, string>? _naming;       // optional channel naming
    private readonly RedisTransportOptions? _options;

    // Pub/Sub subscriptions (QoS 0)
    private readonly ConcurrentDictionary<string, ChannelMessageQueue> _pubSubSubscriptions = new();

    // Stream subscriptions (QoS 1)
    private readonly ConcurrentDictionary<string, Task> _streamTasks = new();
    private readonly CancellationTokenSource _cts = new();

    // Optional auto-batching state
    private readonly ConcurrentQueue<(bool IsStream, string Destination, string Payload, string? TraceParent, string? TraceState)> _batchQueue = new();
    private int _batchQueueCount;
    private Timer? _flushTimer;
    private readonly object _flushLock = new();

    public string Name => "Redis";
    public BatchTransportOptions? BatchOptions => null;
    public CompressionTransportOptions? CompressionOptions => null;

    public RedisMessageTransport(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        string? consumerGroup = null,
        string? consumerName = null,
        IResiliencePipelineProvider? provider = null)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _db = _redis.GetDatabase();
        _subscriber = _redis.GetSubscriber();
        _consumerGroup = consumerGroup ?? $"catga-group-{Environment.MachineName}";
        _consumerName = consumerName ?? $"catga-consumer-{Guid.NewGuid():N}";
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _options = null;
    }

    /// <summary>
    /// Overload that accepts RedisTransportOptions to enable naming convention and channel prefix.
    /// </summary>
    public RedisMessageTransport(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        RedisTransportOptions options,
        string? consumerGroup = null,
        string? consumerName = null,
        IResiliencePipelineProvider? provider = null)
        : this(redis, serializer, consumerGroup, consumerName, provider)
    {
        if (options != null)
        {
            // Ensure trailing dot for simple concatenation
            var prefix = options.ChannelPrefix ?? "catga.";
            _channelPrefix = prefix.EndsWith('.') ? prefix : prefix + ".";
            _naming = options.Naming;
            _options = options;
            if (_options.Batch is { EnableAutoBatching: true } batch)
            {
                _flushTimer = new Timer(static state =>
                {
                    var self = (RedisMessageTransport)state!;
                    try { self.TryFlushBatchTimer(); } catch { /* swallow */ }
                }, this, batch.BatchTimeout, batch.BatchTimeout);
            }
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
            activity.SetTag(CatgaActivitySource.Tags.MessageType, TypeNameCache<TMessage>.Name);
        }

        // Metrics tags
        var tag_component = new KeyValuePair<string, object?>("component", "Transport.Redis");
        var tag_type = new KeyValuePair<string, object?>("message_type", TypeNameCache<TMessage>.Name);
        var tag_dest = new KeyValuePair<string, object?>("destination", subject);

        // Optional auto-batching for Pub/Sub
        if (_options?.Batch is { EnableAutoBatching: true } batchOptions)
        {
            Enqueue(isStream: false, destination: subject, payload: payload, traceParent: null, traceState: null, batchOptions);
            return;
        }

        try
        {
            // Always use Pub/Sub for broadcast messages
            await _provider.ExecuteTransportPublishAsync(ct => new ValueTask(
                _subscriber.PublishAsync(
                    RedisChannel.Literal(subject),
                    payload,
                    CommandFlags.FireAndForget)),
                cancellationToken);
            if (ObservabilityHooks.IsEnabled) CatgaDiagnostics.MessagesPublished.Add(1, tag_component, tag_type, tag_dest);
            System.Diagnostics.Activity.Current?.AddActivityEvent("Redis.Publish.Sent",
                ("channel", subject));
        }
        catch (Exception)
        {
            if (ObservabilityHooks.IsEnabled) CatgaDiagnostics.MessagesFailed.Add(1,
                new KeyValuePair<string, object?>("component", "Transport.Redis"),
                new KeyValuePair<string, object?>("destination", subject));
            System.Diagnostics.Activity.Current?.SetError(System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(new Exception()).SourceException ?? new Exception("Publish failed"));
            System.Diagnostics.Activity.Current?.AddActivityEvent("Redis.Publish.Failed",
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

        if (_options?.Batch is { EnableAutoBatching: true } batchOptions)
        {
            Enqueue(isStream: true, destination: streamKey, payload: payload, traceParent: tp, traceState: ts, batchOptions);
            return;
        }

        try
        {
            // Use Streams for point-to-point messaging
            await _provider.ExecuteTransportSendAsync(ct => new ValueTask(
                _db.StreamAddAsync(
                    streamKey,
                    new NameValueEntry[] {
                        new NameValueEntry("data", payload),
                        tp is null ? default : new NameValueEntry("traceparent", tp),
                        ts is null ? default : new NameValueEntry("tracestate", ts)
                    }.Where(e => e.Name.HasValue).ToArray(),
                    flags: CommandFlags.DemandMaster)),
                cancellationToken);
            if (ObservabilityHooks.IsEnabled) CatgaDiagnostics.MessagesPublished.Add(1, tag_component, tag_type, tag_dest);
            System.Diagnostics.Activity.Current?.AddActivityEvent("Redis.Stream.Added",
                ("stream", streamKey));
        }
        catch (Exception)
        {
            if (ObservabilityHooks.IsEnabled) CatgaDiagnostics.MessagesFailed.Add(1,
                new KeyValuePair<string, object?>("component", "Transport.Redis"),
                new KeyValuePair<string, object?>("destination", streamKey));
            System.Diagnostics.Activity.Current?.SetError(System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(new Exception()).SourceException ?? new Exception("Stream add failed"));
            System.Diagnostics.Activity.Current?.AddActivityEvent("Redis.Stream.Failed",
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
        var queue = await _subscriber.SubscribeAsync(RedisChannel.Literal(subject));
        _pubSubSubscriptions[subject] = queue;

        queue.OnMessage(async channelMessage =>
        {
            using var activity = ObservabilityHooks.IsEnabled ? CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Receive", ActivityKind.Consumer) : null;
            if (activity != null)
            {
                activity.SetTag(CatgaActivitySource.Tags.MessagingSystem, "redis");
                activity.SetTag(CatgaActivitySource.Tags.MessagingDestination, subject);
                activity.SetTag(CatgaActivitySource.Tags.MessageType, TypeNameCache<TMessage>.Name);
            }
            try
            {
                var deserStart = Stopwatch.GetTimestamp();
                var (message, ctx) = DeserializeMessage<TMessage>(channelMessage.Message!);
                var deserMs = (Stopwatch.GetTimestamp() - deserStart) * 1000.0 / Stopwatch.Frequency;
                activity?.AddActivityEvent("Redis.Receive.Deserialized",
                    ("message.type", TypeNameCache<TMessage>.Name),
                    ("duration.ms", deserMs));
                var handlerStart = Stopwatch.GetTimestamp();
                await handler(message, ctx ?? new TransportContext());
                var handlerMs = (Stopwatch.GetTimestamp() - handlerStart) * 1000.0 / Stopwatch.Frequency;
                activity?.AddActivityEvent("Redis.Receive.Handler",
                    ("channel", subject),
                    ("duration.ms", handlerMs));
                activity?.AddActivityEvent("Redis.Receive.Processed",
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

    private string SerializeMessage<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message, TransportContext? context)
        where TMessage : class
    {
        var bytes = _serializer.Serialize(message, typeof(TMessage));
        return Convert.ToBase64String(bytes);
    }

    private (TMessage Message, TransportContext? Context) DeserializeMessage<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(RedisValue data)
        where TMessage : class
    {
        var bytes = Convert.FromBase64String(data.ToString()!);
        var message = (TMessage?)_serializer.Deserialize(bytes, typeof(TMessage));
        return (message!, null);
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

        _flushTimer?.Dispose();
        _cts.Dispose();
    }

    private void Enqueue(bool isStream, string destination, string payload, string? traceParent, string? traceState, BatchTransportOptions batch)
    {
        var newCount = Interlocked.Increment(ref _batchQueueCount);
        if (_options!.MaxQueueLength > 0 && newCount > _options.MaxQueueLength)
        {
            while (Interlocked.CompareExchange(ref _batchQueueCount, _batchQueueCount, _batchQueueCount) > _options.MaxQueueLength && _batchQueue.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _batchQueueCount);
            }
        }
        _batchQueue.Enqueue((isStream, destination, payload, traceParent, traceState));
        if (!isStream)
            System.Diagnostics.Activity.Current?.AddActivityEvent("Redis.Publish.Enqueued",
                ("channel", destination));
        else
            System.Diagnostics.Activity.Current?.AddActivityEvent("Redis.Stream.Enqueued",
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
        var batch = _options?.Batch;
        if (batch is null || !batch.EnableAutoBatching) return;
        if (!Monitor.TryEnter(_flushLock)) return;
        try { FlushInternal(batch).GetAwaiter().GetResult(); }
        finally { Monitor.Exit(_flushLock); }
    }

    private async Task FlushInternal(BatchTransportOptions batch)
    {
        int toProcess = Math.Min(_batchQueueCount, batch.MaxBatchSize);
        if (toProcess <= 0) return;
        var list = new List<(bool IsStream, string Destination, string Payload, string? TraceParent, string? TraceState)>(toProcess);
        while (toProcess-- > 0 && _batchQueue.TryDequeue(out var item))
        {
            Interlocked.Decrement(ref _batchQueueCount);
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
                        _subscriber.PublishAsync(RedisChannel.Literal(item.Destination), item.Payload, CommandFlags.FireAndForget)), _cts.Token);
                    batchSpan?.AddActivityEvent("Redis.Batch.PubSub.Sent",
                        ("channel", item.Destination));
                }
                else
                {
                    var entries = new List<NameValueEntry>(4) { new NameValueEntry("data", item.Payload) };
                    if (!string.IsNullOrEmpty(item.TraceParent)) entries.Add(new NameValueEntry("traceparent", item.TraceParent));
                    if (!string.IsNullOrEmpty(item.TraceState)) entries.Add(new NameValueEntry("tracestate", item.TraceState));
                    await _provider.ExecuteTransportSendAsync(ct => new ValueTask(
                        _db.StreamAddAsync(item.Destination, entries.ToArray(), flags: CommandFlags.DemandMaster)), _cts.Token);
                    batchSpan?.AddActivityEvent("Redis.Batch.Stream.Added",
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
                batchSpan?.AddActivityEvent("Redis.Batch.ItemFailed",
                    ("destination", item.Destination));
            }
        }
    }
}
