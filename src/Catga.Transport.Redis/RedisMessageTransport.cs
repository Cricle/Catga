using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
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
public sealed class RedisMessageTransport : MessageTransportBase, IRequestTimeoutDefaults, IAsyncInitializable, IStoppable, IWaitable, IHealthCheckable, IAsyncDisposable
{
    private const string PubSubEnvelopePrefix = "ctx1";
    private readonly record struct BatchedRedisPublish(string Payload, TransportContext Context);

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
    public override BatchTransportOptions? BatchOptions => _opts?.Batch;
    public TimeSpan DefaultRequestTimeout => _opts?.RequestTimeout ?? TimeSpan.FromSeconds(30);
    
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
        => await PublishToSubjectAsync(message, GetSubject<TMessage>(), context, cancellationToken);

    public override async Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        string destination,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        => await SendToDestinationAsync(message, ResolveDestination<TMessage>(destination), context, cancellationToken);

    public override Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken cancellationToken = default)
        => SubscribeCoreAsync(GetSubject<TMessage>(), handler, cancellationToken);

    public override Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        string destination,
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken cancellationToken = default)
        => SubscribeCoreAsync(ResolveDestination<TMessage>(destination), handler, cancellationToken);

    private async Task PublishToSubjectAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        string subject,
        TransportContext? context,
        CancellationToken cancellationToken)
        where TMessage : class
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
            var effectiveContext = TransportMessageContextAccessor.EnrichOutgoing(message, context);
            TransportMessageContextAccessor.ApplyToMessage(message, effectiveContext);

            using var activity = StartPublishActivity("redis", subject, TypeNameCache<TMessage>.Name);
            var current = Activity.Current;
            var traceParent = ObservabilityHooks.IsEnabled ? current?.Id : null;
            var traceState = ObservabilityHooks.IsEnabled ? current?.TraceStateString : null;
            var payload = SerializePubSubPayload(message, effectiveContext, traceParent, traceState);

            // QoS2 deduplication check
            var msg = message as IMessage;
            var qos = msg?.QoS ?? QualityOfService.AtLeastOnce;
            if (qos == QualityOfService.ExactlyOnce && effectiveContext.MessageId.HasValue)
            {
                var dedupKey = $"dedup:{effectiveContext.MessageId.Value}";
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
                EnqueueBatch(
                    new BatchItem(
                        subject,
                        [],
                        traceParent,
                        traceState,
                        new BatchedRedisPublish(payload, effectiveContext)),
                    batchOptions,
                    _opts.MaxQueueLength);
                return;
            }

            try
            {
                await ResilienceProvider.ExecuteTransportPublishAsync(
                    _ => new ValueTask(GetSubscriber().PublishAsync(RedisChannel.Literal(subject), payload, CommandFlags.FireAndForget)),
                    cancellationToken);
                if (TryResolveReplyChannel(effectiveContext.Metadata, out var replySubject))
                {
                    await ResilienceProvider.ExecuteTransportPublishAsync(
                        _ => new ValueTask(GetSubscriber().PublishAsync(RedisChannel.Literal(replySubject), payload, CommandFlags.FireAndForget)),
                        cancellationToken);
                }
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

    private async Task SendToDestinationAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        string destination,
        TransportContext? context,
        CancellationToken cancellationToken)
        where TMessage : class
        => await SendToDestinationAsync(message, destination, context, cancellationToken, trackPendingOperation: true);

    private async Task SendToDestinationAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        string destination,
        TransportContext? context,
        CancellationToken cancellationToken,
        bool trackPendingOperation)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_acceptingMessages)
            throw new InvalidOperationException("Transport is not accepting new messages");

        if (trackPendingOperation)
            Interlocked.Increment(ref _pendingOperations);
        try
        {
            var effectiveContext = TransportMessageContextAccessor.EnrichOutgoing(message, context);
            TransportMessageContextAccessor.ApplyToMessage(message, effectiveContext);
            var payload = SerializeToBase64(message);
            var streamKey = $"stream:{destination}";
            var current = Activity.Current;
            var traceParent = ObservabilityHooks.IsEnabled ? current?.Id : null;
            var traceState = ObservabilityHooks.IsEnabled ? current?.TraceStateString : null;

            using var activity = StartPublishActivity("redis", streamKey, TypeNameCache<TMessage>.Name);

            try
            {
                var entries = BuildStreamEntries(payload, effectiveContext, traceParent, traceState);
                await ResilienceProvider.ExecuteTransportSendAsync(
                    _ => new ValueTask(GetDatabase().StreamAddAsync(streamKey, entries, flags: CommandFlags.DemandMaster)),
                    cancellationToken);
                RecordPublishSuccess(TypeNameCache<TMessage>.Name, streamKey);
                activity?.AddActivityEvent(RedisActivityEvents.RedisBatchStreamAdded, ("stream", streamKey));
            }
            catch (Exception ex)
            {
                RecordPublishFailure(streamKey, "send");
                activity?.SetError(ex);
                throw;
            }
        }
        finally
        {
            if (trackPendingOperation)
                Interlocked.Decrement(ref _pendingOperations);
        }
    }

    private async Task SubscribeCoreAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        string subject,
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken cancellationToken)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(handler);

        var queue = await GetSubscriber().SubscribeAsync(RedisChannel.Literal(subject));
        _pubSubs[subject] = queue;

        queue.OnMessage(async channelMessage =>
        {
            string? traceParent = null;
            string? traceState = null;
            TryParsePubSubEnvelope(channelMessage.Message!, out _, out _, out traceParent, out traceState);

            using var activity = StartReceiveActivity("redis", subject, TypeNameCache<TMessage>.Name, traceParent, traceState);
            try
            {
                var deserStart = Stopwatch.GetTimestamp();
                var message = DeserializePubSubPayload<TMessage>(
                    channelMessage.Message!,
                    out var context,
                    out _,
                    out _);
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
                using var scope = TransportMessageContextAccessor.Push(context);
                await handler(message, context);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string ResolveDestination<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(string destination)
        where TMessage : class
    {
        if (string.Equals(destination, TypeNameCache<TMessage>.Name, StringComparison.Ordinal))
            return GetSubject<TMessage>();

        return destination.TrimStart('.');
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
            var name = nv.Name.ToString();
            if (string.Equals(name, "traceparent", StringComparison.OrdinalIgnoreCase)) tp = nv.Value;
            else if (string.Equals(name, "tracestate", StringComparison.OrdinalIgnoreCase)) ts = nv.Value;
            else if (string.Equals(name, "data", StringComparison.OrdinalIgnoreCase)) dataStr = nv.Value;
        }

        using var activity = StartReceiveActivity("redis", streamKey, TypeNameCache<TMessage>.Name, tp, ts);

        try
        {
            var deserStart = Stopwatch.GetTimestamp();
            var message = DeserializeFromBase64<TMessage>(dataStr!);
            var deserMs = (Stopwatch.GetTimestamp() - deserStart) * 1000.0 / Stopwatch.Frequency;
            var payloadSize = 0;
            try { payloadSize = Convert.FromBase64String(dataStr!).Length; } catch { }
            var context = BuildContextFromStreamEntry<TMessage>(entry);
            activity?.AddActivityEvent(RedisActivityEvents.RedisReceiveDeserialized,
                ("message.type", TypeNameCache<TMessage>.Name), ("duration.ms", deserMs), ("payload.size", payloadSize));

            var handlerStart = Stopwatch.GetTimestamp();
            using var scope = TransportMessageContextAccessor.Push(context);
            await handler(message, context);
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
            var (isStream, payload, context) = item.Extra switch
            {
                (bool s, string p) => (s, p, new TransportContext()),
                BatchedRedisPublish publish => (false, publish.Payload, publish.Context),
                _ => (false, string.Empty, new TransportContext())
            };
            try
            {
                if (!isStream)
                {
                    await ResilienceProvider.ExecuteTransportPublishAsync(
                        _ => new ValueTask(GetSubscriber().PublishAsync(RedisChannel.Literal(item.Destination), payload, CommandFlags.FireAndForget)),
                        Cts.Token);
                    if (TryResolveReplyChannel(context.Metadata, out var replySubject))
                    {
                        await ResilienceProvider.ExecuteTransportPublishAsync(
                            _ => new ValueTask(GetSubscriber().PublishAsync(RedisChannel.Literal(replySubject), payload, CommandFlags.FireAndForget)),
                            Cts.Token);
                    }
                    batchSpan?.AddActivityEvent(RedisActivityEvents.RedisBatchPubSubSent, ("channel", item.Destination));
                }
                else
                {
                    var entries = BuildStreamEntries(payload, new TransportContext(), item.TraceParent, item.TraceState);
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

    public override async Task<TResponse?> RequestAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(
        TMessage message,
        string destination,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        where TMessage : class
        where TResponse : class
    {
        if (!_acceptingMessages)
            throw new InvalidOperationException("Transport is not accepting new messages");

        Interlocked.Increment(ref _pendingOperations);
        try
        {
            var correlationId = MessageExtensions.NewCorrelationId();
            var replySubject = $"{Prefix}reply.{correlationId}";
            var pending = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var queue = await GetSubscriber().SubscribeAsync(RedisChannel.Literal(replySubject));
            queue.OnMessage(channelMessage => pending.TrySetResult(DecodePublishedPayload(channelMessage.Message!, out _)));

            var context = TransportMessageContextAccessor.EnrichOutgoing(message, new TransportContext
            {
                MessageId = MessageExtensions.NewMessageId(),
                CorrelationId = correlationId,
                MessageType = TypeNameCache<TMessage>.FullName,
                SentAt = DateTime.UtcNow,
                Metadata = new Dictionary<string, string>
                {
                    ["reply_to"] = replySubject,
                    ["reply_subject"] = replySubject
                }
            });

            try
            {
                await SendToDestinationAsync(
                    message,
                    ResolveDestination<TMessage>(destination),
                    context,
                    cancellationToken,
                    trackPendingOperation: false);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout);
                cts.Token.Register(() => pending.TrySetCanceled(cts.Token));
                var bytes = await pending.Task.ConfigureAwait(false);
                return Serializer.Deserialize<TResponse>(bytes);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            finally
            {
                queue.Unsubscribe();
            }
        }
        finally
        {
            Interlocked.Decrement(ref _pendingOperations);
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
        var bytes = Serializer.Serialize(message);
        return Convert.ToBase64String(bytes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TMessage DeserializeFromBase64<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(string data) where TMessage : class
    {
        var bytes = Convert.FromBase64String(data);
        return (TMessage)Serializer.Deserialize(bytes, typeof(TMessage))!;
    }

    private string SerializePubSubPayload<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        TransportContext context,
        string? traceParent,
        string? traceState)
        where TMessage : class
    {
        var payload = SerializeToBase64(message);
        return BuildPubSubEnvelope(payload, context, traceParent, traceState);
    }

    private TMessage DeserializePubSubPayload<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        string data,
        out TransportContext context,
        out string? traceParent,
        out string? traceState)
        where TMessage : class
    {
        if (!TryParsePubSubEnvelope(data, out var payload, out context, out traceParent, out traceState))
        {
            var message = DeserializeFromBase64<TMessage>(data);
            context = BuildContextFromMessage(message);
            traceParent = null;
            traceState = null;
            return message;
        }

        var bytes = Convert.FromBase64String(payload);
        return (TMessage)Serializer.Deserialize(bytes, typeof(TMessage))!;
    }

    private static byte[] DecodePublishedPayload(string data, out TransportContext context)
    {
        if (TryParsePubSubEnvelope(data, out var payload, out context, out _, out _))
            return Convert.FromBase64String(payload);

        context = default;
        return Convert.FromBase64String(data);
    }

    private static string BuildPubSubEnvelope(
        string payload,
        TransportContext context,
        string? traceParent,
        string? traceState)
    {
        return string.Join('|',
            PubSubEnvelopePrefix,
            payload,
            context.MessageId?.ToString() ?? string.Empty,
            context.CorrelationId?.ToString() ?? string.Empty,
            EncodeEnvelopeString(context.MessageType),
            EncodeEnvelopeString(context.SentAt?.ToString("O")),
            EncodeEnvelopeMetadata(context.Metadata),
            EncodeEnvelopeString(traceParent),
            EncodeEnvelopeString(traceState));
    }

    private static bool TryParsePubSubEnvelope(
        string data,
        out string payload,
        out TransportContext context)
        => TryParsePubSubEnvelope(data, out payload, out context, out _, out _);

    private static bool TryParsePubSubEnvelope(
        string data,
        out string payload,
        out TransportContext context,
        out string? traceParent,
        out string? traceState)
    {
        payload = string.Empty;
        context = default;
        traceParent = null;
        traceState = null;

        if (string.IsNullOrEmpty(data) || !data.StartsWith($"{PubSubEnvelopePrefix}|", StringComparison.Ordinal))
            return false;

        var parts = data.Split('|', 9, StringSplitOptions.None);
        if ((parts.Length != 7 && parts.Length != 9) || !string.Equals(parts[0], PubSubEnvelopePrefix, StringComparison.Ordinal))
            return false;

        payload = parts[1];
        context = new TransportContext
        {
            MessageId = TryParseLong(parts[2]),
            CorrelationId = TryParseLong(parts[3]),
            MessageType = DecodeEnvelopeString(parts[4]),
            SentAt = TryParseDateTime(parts[5]),
            Metadata = DecodeEnvelopeMetadata(parts[6])
        };
        if (parts.Length >= 8)
            traceParent = DecodeEnvelopeString(parts[7]);
        if (parts.Length >= 9)
            traceState = DecodeEnvelopeString(parts[8]);
        return true;
    }

    private static long? TryParseLong(string value)
        => long.TryParse(value, out var parsed) ? parsed : null;

    private static DateTime? TryParseDateTime(string encoded)
    {
        var value = DecodeEnvelopeString(encoded);
        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }

    private static string EncodeEnvelopeString(string? value)
        => string.IsNullOrEmpty(value)
            ? string.Empty
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string? DecodeEnvelopeString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        return Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }

    private static string EncodeEnvelopeMetadata(Dictionary<string, string>? metadata)
    {
        if (metadata is not { Count: > 0 })
            return string.Empty;

        return string.Join(',',
            metadata.Select(pair => $"{EncodeEnvelopeString(pair.Key)}:{EncodeEnvelopeString(pair.Value)}"));
    }

    private static Dictionary<string, string>? DecodeEnvelopeMetadata(string value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split(':', 2, StringSplitOptions.None);
            if (parts.Length != 2)
                continue;

            var key = DecodeEnvelopeString(parts[0]);
            var itemValue = DecodeEnvelopeString(parts[1]);
            if (string.IsNullOrEmpty(key) || itemValue is null)
                continue;

            metadata[key] = itemValue;
        }

        return metadata.Count > 0 ? NormalizeReplyMetadata(metadata) : null;
    }

    private static NameValueEntry[] BuildStreamEntries(string payload, TransportContext context, string? tp, string? ts)
    {
        var entries = new List<NameValueEntry>(6)
        {
            new("data", payload)
        };

        if (context.MessageId.HasValue)
            entries.Add(new("message_id", context.MessageId.Value.ToString()));
        if (context.CorrelationId.HasValue)
            entries.Add(new("correlation_id", context.CorrelationId.Value.ToString()));
        if (!string.IsNullOrEmpty(context.MessageType))
            entries.Add(new("message_type", context.MessageType));
        if (context.SentAt.HasValue)
            entries.Add(new("sent_at", context.SentAt.Value.ToString("O")));
        if (context.Metadata is { Count: > 0 })
        {
            foreach (var pair in context.Metadata)
                entries.Add(new($"meta.{pair.Key}", pair.Value));
        }
        if (!string.IsNullOrEmpty(tp))
            entries.Add(new("traceparent", tp));
        if (!string.IsNullOrEmpty(ts))
            entries.Add(new("tracestate", ts));

        return entries.ToArray();
    }

    private static TransportContext BuildContextFromMessage(object message)
        => new()
        {
            MessageId = TransportMessageContextAccessor.TryGetMessageId(message),
            CorrelationId = TransportMessageContextAccessor.TryGetCorrelationId(message),
            MessageType = message.GetType().FullName
        };

    private static TransportContext BuildContextFromStreamEntry<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(StreamEntry entry)
        where TMessage : class
    {
        long? messageId = null;
        long? correlationId = null;
        string? messageType = TypeNameCache<TMessage>.FullName;
        DateTime? sentAt = null;
        Dictionary<string, string>? metadata = null;

        foreach (var nv in entry.Values)
        {
            var name = nv.Name.ToString();
            if (MatchesFieldName(name, "messageid") &&
                long.TryParse(nv.Value.ToString(), out var parsedMessageId))
                messageId = parsedMessageId;
            else if (MatchesFieldName(name, "correlationid") &&
                     long.TryParse(nv.Value.ToString(), out var parsedCorrelationId))
                correlationId = parsedCorrelationId;
            else if (MatchesFieldName(name, "messagetype"))
                messageType = nv.Value;
            else if (MatchesFieldName(name, "sentat") &&
                     DateTime.TryParse(
                nv.Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsedSentAt))
                sentAt = parsedSentAt;
            else if (TryGetMetadataKey(name, out var metadataKey))
            {
                metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
                metadata[metadataKey] = nv.Value!;
            }
        }

        metadata = NormalizeReplyMetadata(metadata);

        return new TransportContext
        {
            MessageId = messageId,
            CorrelationId = correlationId,
            MessageType = messageType,
            SentAt = sentAt,
            Metadata = metadata
        };
    }

    private static bool TryResolveReplyChannel(Dictionary<string, string>? metadata, out string replyChannel)
    {
        replyChannel = string.Empty;
        if (metadata is not { Count: > 0 })
            return false;

        if (metadata.TryGetValue("reply_to", out var replyTo) && !string.IsNullOrWhiteSpace(replyTo))
        {
            replyChannel = replyTo;
            return true;
        }

        if (metadata.TryGetValue("reply_subject", out var replySubject) && !string.IsNullOrWhiteSpace(replySubject))
        {
            replyChannel = replySubject;
            return true;
        }

        return false;
    }

    private static Dictionary<string, string>? NormalizeReplyMetadata(Dictionary<string, string>? metadata)
    {
        if (metadata is not { Count: > 0 })
            return metadata;

        NormalizeReplyAlias(metadata, "reply-to", "reply_to");
        NormalizeReplyAlias(metadata, "reply-subject", "reply_subject");

        if (metadata.TryGetValue("reply_to", out var replyTo) && !string.IsNullOrWhiteSpace(replyTo))
            metadata.TryAdd("reply_subject", replyTo);
        else if (metadata.TryGetValue("reply_subject", out var replySubject) && !string.IsNullOrWhiteSpace(replySubject))
            metadata.TryAdd("reply_to", replySubject);

        return metadata;
    }

    private static void NormalizeReplyAlias(Dictionary<string, string> metadata, string alias, string canonical)
    {
        if (TryGetMetadataValue(metadata, canonical, out _))
            return;

        if (TryGetMetadataValue(metadata, alias, out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            metadata[canonical] = value;
        }
    }

    private static bool TryGetMetadataValue(
        Dictionary<string, string> metadata,
        string key,
        [NotNullWhen(true)] out string? value)
    {
        if (metadata.TryGetValue(key, out value))
            return true;

        foreach (var entry in metadata)
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = entry.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryGetMetadataKey(string key, [NotNullWhen(true)] out string? metadataKey)
    {
        if (key.Length > 5 &&
            key.StartsWith("meta", StringComparison.OrdinalIgnoreCase) &&
            key[4] is '.' or '-' or '_')
        {
            metadataKey = key[5..];
            return !string.IsNullOrWhiteSpace(metadataKey);
        }

        metadataKey = null;
        return false;
    }

    private static bool MatchesFieldName(string key, string normalizedExpected)
    {
        if (string.Equals(key, normalizedExpected, StringComparison.OrdinalIgnoreCase))
            return true;

        Span<char> normalized = stackalloc char[key.Length];
        var length = 0;
        foreach (var ch in key)
        {
            if (ch is '_' or '-')
                continue;

            normalized[length++] = char.ToLowerInvariant(ch);
        }

        return normalized[..length].SequenceEqual(normalizedExpected.AsSpan());
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
        {
            try
            {
                await Task.WhenAll(_streams.Values);
            }
            catch (OperationCanceledException)
            {
                // Disposal cancels stream workers; treat it as a clean shutdown.
            }
        }
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
