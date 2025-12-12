using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Abstractions;
using Catga.Configuration;
using Catga.Core;
using Catga.Observability;
using Catga.Resilience;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Catga.Transport.Nats;

/// <summary>NATS transport with QoS support (Core Pub/Sub + JetStream).</summary>
public class NatsMessageTransport(INatsConnection connection, IMessageSerializer serializer, ILogger<NatsMessageTransport> logger, IResiliencePipelineProvider provider, NatsTransportOptions? options = null)
    : IMessageTransport, IAsyncDisposable
{
    private readonly NatsJSContext _js = new(connection);
    private readonly string _prefix = (options?.SubjectPrefix ?? "catga").TrimEnd('.');
    private readonly Func<Type, string>? _naming = options?.Naming;
    private readonly ConcurrentDictionary<string, Task> _subs = new();
    private readonly ConcurrentDictionary<long, long> _dedup = new();
    private readonly ConcurrentQueue<long> _dedupOrder = new();
    private readonly ConcurrentQueue<(string Subject, byte[] Payload, NatsHeaders Headers, QualityOfService QoS)> _batch = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _jsLock = new();
    private readonly object _flushLock = new();
    private volatile bool _jsReady;
    private int _batchCount;
    private readonly Timer? _timer = options?.Batch is { EnableAutoBatching: true } b
        ? new Timer(static s => { try { ((NatsMessageTransport)s!).TryFlushBatchTimer(); } catch { } }, null, b.BatchTimeout, b.BatchTimeout)
        : null;

    public string Name => "NATS";
    public BatchTransportOptions? BatchOptions => null;
    public CompressionTransportOptions? CompressionOptions => null;
    private const int DedupCap = 10000;
    private static readonly TimeSpan DedupTtl = TimeSpan.FromMinutes(2);

    private void CleanupDedup(long nowTicks)
    {
        var evicted = 0;
        foreach (var kv in _dedup)
        {
            if (nowTicks - kv.Value > DedupTtl.Ticks)
            {
                if (_dedup.TryRemove(kv.Key, out _)) evicted++;
            }
        }
        if (evicted > 0) CatgaDiagnostics.NatsDedupEvictions.Add(evicted);
    }

    private void EvictIfNeeded()
    {
        var evicted = 0;
        while (_dedup.Count > DedupCap && _dedupOrder.TryDequeue(out var old))
        {
            if (_dedup.TryRemove(old, out _)) evicted++;
        }
        if (evicted > 0) CatgaDiagnostics.NatsDedupEvictions.Add(evicted);
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts.Cancel(); } catch { }
        if (!_subs.IsEmpty)
        {
            try { await Task.WhenAll(_subs.Values); } catch { /* ignore */ }
            _subs.Clear();
        }
        _timer?.Dispose();
        _cts.Dispose();
    }

    public NatsMessageTransport(INatsConnection connection, IMessageSerializer serializer, ILogger<NatsMessageTransport> logger, IResiliencePipelineProvider provider, CatgaOptions globalOptions, NatsTransportOptions? options = null)
        : this(connection, serializer, logger, provider, options)
    {
        if (_naming is null && globalOptions?.EndpointNamingConvention is not null)
            _naming = globalOptions.EndpointNamingConvention;
    }

    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
    {
        var subject = GetSubjectCached<TMessage>();
        var qos = message is IEvent ev
            ? ev.QoS
            : (message as IMessage)?.QoS ?? QualityOfService.AtLeastOnce;
        var ctx = context ?? new TransportContext { MessageId = MessageExtensions.NewMessageId(), MessageType = TypeNameCache<TMessage>.FullName, SentAt = DateTime.UtcNow };

        using var activity = ObservabilityHooks.IsEnabled ? CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Publish", ActivityKind.Producer) : null;
        if (activity != null)
        {
            activity.SetTag(CatgaActivitySource.Tags.MessagingSystem, "nats");
            activity.SetTag(CatgaActivitySource.Tags.MessagingDestination, subject);
            activity.SetTag(CatgaActivitySource.Tags.MessagingOperation, "publish");
            activity.SetTag(CatgaActivitySource.Tags.MessageType, TypeNameCache<TMessage>.Name);
            activity.SetTag(CatgaActivitySource.Tags.MessageId, ctx.MessageId);
            var qosString = qos switch { QualityOfService.AtMostOnce => "AtMostOnce", QualityOfService.AtLeastOnce => "AtLeastOnce", QualityOfService.ExactlyOnce => "ExactlyOnce", _ => "Unknown" };
            activity.SetTag(CatgaActivitySource.Tags.QoS, qosString);
        }

        var payload = serializer.Serialize(message!, typeof(TMessage));
        var headers = new NatsHeaders
        {
            ["MessageId"] = ctx.MessageId?.ToString() ?? string.Empty,
            ["MessageType"] = ctx.MessageType ?? TypeNameCache<TMessage>.FullName,
            ["SentAt"] = ctx.SentAt?.ToString("O") ?? DateTime.UtcNow.ToString("O"),
            ["QoS"] = ((int)qos).ToString()
        };
        if (ctx.CorrelationId.HasValue)
            headers["CorrelationId"] = ctx.CorrelationId.Value.ToString();
        // W3C trace propagation (if any)
        var current = Activity.Current;
        if (ObservabilityHooks.IsEnabled && current != null)
        {
            headers["traceparent"] = current.Id;
            if (!string.IsNullOrEmpty(current.TraceStateString))
                headers["tracestate"] = current.TraceStateString;
        }

        var tag_component = new KeyValuePair<string, object?>("component", "Transport.NATS");
        var tag_type = new KeyValuePair<string, object?>("message_type", TypeNameCache<TMessage>.Name);
        var tag_dest = new KeyValuePair<string, object?>("destination", subject);
        var tag_qos = new KeyValuePair<string, object?>("qos", ((int)qos).ToString());

        // Optional auto-batching
        if (options?.Batch is { EnableAutoBatching: true } batchOptions)
        {
            Enqueue(subject, payload, headers, qos, batchOptions);
            System.Diagnostics.Activity.Current?.AddActivityEvent(CatgaActivitySource.Events.NatsPublishEnqueued,
                ("subject", subject),
                ("qos", (int)qos));
            return; // queued for background flush
        }

        try
        {
            // Delegate QoS handling to NATS native capabilities
            switch (qos)
            {
                case QualityOfService.AtMostOnce:
                    // NATS Core Pub/Sub: fire-and-forget, no ack, no persistence
                    await provider.ExecuteTransportPublishAsync(ct =>
                        connection.PublishAsync(subject, payload, headers: headers, cancellationToken: ct),
                        cancellationToken);
                    CatgaLog.NatsPublishedCore(logger, ctx.MessageId);
                    System.Diagnostics.Activity.Current?.AddActivityEvent(CatgaActivitySource.Events.NatsPublishSent,
                        ("subject", subject),
                        ("qos", (int)qos));
                    break;

                case QualityOfService.AtLeastOnce:
                    await EnsureStreamAsync(cancellationToken);
                    // JetStream: guaranteed delivery, may duplicate (consumer acks)
                    var ack1 = await provider.ExecuteTransportPublishAsync(ct =>
                        _js!.PublishAsync(subject: subject, data: payload, opts: new NatsJSPubOpts { MsgId = ctx.MessageId?.ToString() }, headers: headers, cancellationToken: ct),
                        cancellationToken);
                    CatgaLog.NatsPublishedQoS1(logger, ctx.MessageId, ack1.Seq, ack1.Duplicate);
                    System.Diagnostics.Activity.Current?.AddActivityEvent(CatgaActivitySource.Events.NatsPublishSent,
                        ("subject", subject),
                        ("qos", (int)qos),
                        ("seq", (long)ack1.Seq),
                        ("dup", ack1.Duplicate));
                    break;

                case QualityOfService.ExactlyOnce:
                    await EnsureStreamAsync(cancellationToken);
                    // JetStream with MsgId deduplication: exactly-once using NATS native dedup window (default 2 minutes)
                    // Note: Application-level idempotency (via IdempotencyBehavior) provides additional business logic dedup
                    var ack2 = await provider.ExecuteTransportPublishAsync(ct =>
                        _js!.PublishAsync(subject: subject, data: payload, opts: new NatsJSPubOpts { MsgId = ctx.MessageId?.ToString() }, headers: headers, cancellationToken: ct),
                        cancellationToken);
                    CatgaLog.NatsPublishedQoS2(logger, ctx.MessageId, ack2.Seq, ack2.Duplicate);
                    System.Diagnostics.Activity.Current?.AddActivityEvent(CatgaActivitySource.Events.NatsPublishSent,
                        ("subject", subject),
                        ("qos", (int)qos),
                        ("seq", (long)ack2.Seq),
                        ("dup", ack2.Duplicate));
                    break;
            }

            if (ObservabilityHooks.IsEnabled)
            {
                CatgaDiagnostics.MessagesPublished.Add(1, tag_component, tag_type, tag_dest, tag_qos);
            }
        }
        catch (Exception ex)
        {
            if (ObservabilityHooks.IsEnabled)
            {
                CatgaDiagnostics.MessagesFailed.Add(1,
                    new KeyValuePair<string, object?>("component", "Transport.NATS"),
                    new KeyValuePair<string, object?>("destination", subject),
                    new KeyValuePair<string, object?>("reason", "publish"));
            }
            CatgaLog.NatsPublishFailed(logger, ex, subject, ctx.MessageId);
            System.Diagnostics.Activity.Current?.SetError(ex);
            System.Diagnostics.Activity.Current?.AddActivityEvent(CatgaActivitySource.Events.NatsPublishFailed,
                ("subject", subject));
            throw;
        }
    }

    public Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message, string destination, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
        => PublishAsync(message, context, cancellationToken);

    public Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(Func<TMessage, TransportContext, Task> handler, CancellationToken cancellationToken = default) where TMessage : class
    {
        var subject = GetSubjectCached<TMessage>();
        var task = Task.Run(async () =>
        {
            var ct = _cts.Token;
            await foreach (var msg in connection.SubscribeAsync<byte[]>(subject, cancellationToken: ct))
            {
                Activity? activity = null;
                try
                {
                    // Try to restore parent from W3C headers if present
                    var tp = msg.Headers?["traceparent"].ToString();
                    if (ObservabilityHooks.IsEnabled && !string.IsNullOrEmpty(tp))
                    {
                        var ts = msg.Headers?["tracestate"].ToString();
                        try
                        {
                            var parent = ActivityContext.Parse(tp!, ts);
                            activity = CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Receive", ActivityKind.Consumer, parent);
                        }
                        catch
                        {
                            activity = ObservabilityHooks.IsEnabled ? CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Receive", ActivityKind.Consumer) : null;
                        }
                    }
                    else
                    {
                        activity = ObservabilityHooks.IsEnabled ? CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Receive", ActivityKind.Consumer) : null;
                    }
                    if (activity != null)
                    {
                        activity.SetTag(CatgaActivitySource.Tags.MessagingSystem, "nats");
                        activity.SetTag(CatgaActivitySource.Tags.MessagingDestination, subject);
                        activity.SetTag(CatgaActivitySource.Tags.MessagingOperation, "receive");
                    }
                    if (msg.Data == null || msg.Data.Length == 0)
                    {
                        CatgaLog.NatsEmptyMessage(logger, subject);
                        activity?.AddActivityEvent(CatgaActivitySource.Events.NatsReceiveEmpty,
                            ("subject", subject));
                        if (ObservabilityHooks.IsEnabled)
                        {
                            CatgaDiagnostics.MessagesFailed.Add(1,
                                new KeyValuePair<string, object?>("component", "Transport.NATS"),
                                new KeyValuePair<string, object?>("destination", subject),
                                new KeyValuePair<string, object?>("reason", "empty"));
                        }
                        continue;
                    }
                    var deserStart = Stopwatch.GetTimestamp();
                    var message = serializer.Deserialize<TMessage>(msg.Data);
                    var deserMs = (Stopwatch.GetTimestamp() - deserStart) * 1000.0 / Stopwatch.Frequency;
                    activity?.AddActivityEvent(CatgaActivitySource.Events.NatsReceiveDeserialized,
                        ("message.type", typeof(TMessage).Name),
                        ("duration.ms", deserMs),
                        ("payload.size", msg.Data.Length));
                    if (activity != null)
                    {
                        var headerType = msg.Headers?["MessageType"].ToString();
                        if (!string.IsNullOrEmpty(headerType)) activity.SetTag(CatgaActivitySource.Tags.MessageType, headerType);
                    }
                    if (message == null)
                    {
                        CatgaLog.NatsDeserializeFailed(logger, subject);
                        if (ObservabilityHooks.IsEnabled)
                        {
                            CatgaDiagnostics.MessagesFailed.Add(1,
                                new KeyValuePair<string, object?>("component", "Transport.NATS"),
                                new KeyValuePair<string, object?>("destination", subject),
                                new KeyValuePair<string, object?>("reason", "deserialize"));
                        }
                        continue;
                    }
                    var sentAtValue = msg.Headers?["SentAt"];
                    DateTime? sentAt = null;
                    if (sentAtValue.HasValue && DateTime.TryParse(sentAtValue.Value.ToString(), out var parsed))
                        sentAt = parsed;
                    long? messageId = null;
                    if (msg.Headers?["MessageId"] is var msgIdHeader && long.TryParse(msgIdHeader.ToString(), out var parsedMsgId))
                        messageId = parsedMsgId;

                    long? correlationId = null;
                    if (msg.Headers?["CorrelationId"] is var corrIdHeader && long.TryParse(corrIdHeader.ToString(), out var parsedCorrId))
                        correlationId = parsedCorrId;

                    // Dedup for QoS0 (AtMostOnce) and QoS2 (ExactlyOnce)
                    int qosHeader = 0;
                    if (msg.Headers?["QoS"] is var qosVal && int.TryParse(qosVal.ToString(), out var parsedQos))
                        qosHeader = parsedQos;
                    if ((qosHeader == (int)QualityOfService.ExactlyOnce || qosHeader == (int)QualityOfService.AtMostOnce) && messageId.HasValue)
                    {
                        var now = DateTime.UtcNow.Ticks;
                        if (_dedup.TryGetValue(messageId.Value, out var ts))
                        {
                            if (now - ts <= DedupTtl.Ticks)
                            {
                                CatgaLog.NatsDroppedDuplicate(logger, messageId, qosHeader, subject);
                                CatgaDiagnostics.NatsDedupDrops.Add(1);
                                activity?.AddActivityEvent(CatgaActivitySource.Events.NatsReceiveDroppedDuplicate,
                                    ("message.id", messageId),
                                    ("qos", qosHeader));
                                continue;
                            }
                            _dedup[messageId.Value] = now;
                            _dedupOrder.Enqueue(messageId.Value);
                        }
                        else
                        {
                            if (_dedup.TryAdd(messageId.Value, now))
                                _dedupOrder.Enqueue(messageId.Value);
                        }
                        if (_dedup.Count > DedupCap)
                        {
                            CleanupDedup(now);
                            EvictIfNeeded();
                        }
                    }

                    var context = new TransportContext
                    {
                        MessageId = messageId,
                        MessageType = msg.Headers?["MessageType"],
                        CorrelationId = correlationId,
                        SentAt = sentAt
                    };
                    var handlerStart = Stopwatch.GetTimestamp();
                    await handler(message, context);
                    var handlerMs = (Stopwatch.GetTimestamp() - handlerStart) * 1000.0 / Stopwatch.Frequency;
                    activity?.AddActivityEvent(CatgaActivitySource.Events.NatsReceiveHandler,
                        ("subject", subject),
                        ("duration.ms", handlerMs));
                    activity?.AddActivityEvent(CatgaActivitySource.Events.NatsReceiveProcessed,
                        ("subject", subject));
                }
                catch (Exception ex) { CatgaLog.NatsProcessingError(logger, ex, subject); activity?.SetError(ex); }
            }
        }, cancellationToken);
        _subs[subject] = task;
        return Task.CompletedTask;
    }

    private void Enqueue(string subject, byte[] payload, NatsHeaders headers, QualityOfService qos, BatchTransportOptions batch)
    {
        // Backpressure: drop oldest when exceeding MaxQueueLength
        var newCount = Interlocked.Increment(ref _batchCount);
        if (options!.MaxQueueLength > 0 && newCount > options.MaxQueueLength)
        {
            while (Interlocked.CompareExchange(ref _batchCount, _batchCount, _batchCount) > options.MaxQueueLength && _batch.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _batchCount);
            }
        }
        _batch.Enqueue((subject, payload, headers, qos));

        // Immediate flush by size
        if (newCount >= batch.MaxBatchSize)
        {
            TryFlushBatchImmediate(batch);
        }
    }

    private void TryFlushBatchImmediate(BatchTransportOptions batch)
    {
        if (!Monitor.TryEnter(_flushLock)) return;
        try
        {
            FlushInternal(batch).GetAwaiter().GetResult();
        }
        finally
        {
            Monitor.Exit(_flushLock);
        }
    }

    private void TryFlushBatchTimer()
    {
        var batch = options?.Batch;
        if (batch is null || !batch.EnableAutoBatching) return;
        if (!Monitor.TryEnter(_flushLock)) return;
        try
        {
            FlushInternal(batch).GetAwaiter().GetResult();
        }
        finally
        {
            Monitor.Exit(_flushLock);
        }
    }

    private async Task FlushInternal(BatchTransportOptions batch)
    {
        int toProcess = Math.Min(_batchCount, batch.MaxBatchSize);
        if (toProcess <= 0) return;

        var list = new List<(string Subject, byte[] Payload, NatsHeaders Headers, QualityOfService QoS)>(toProcess);
        while (toProcess-- > 0 && _batch.TryDequeue(out var item))
        {
            Interlocked.Decrement(ref _batchCount);
            list.Add(item);
        }

        using var batchSpan = ObservabilityHooks.IsEnabled ? CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Batch.Flush", ActivityKind.Producer) : null;
        foreach (var (Subject, Payload, Headers, QoS) in list)
        {
            try
            {
                switch (QoS)
                {
                    case QualityOfService.AtMostOnce:
                        await provider.ExecuteTransportPublishAsync(ct =>
                            connection.PublishAsync(Subject, Payload, headers: Headers, cancellationToken: ct),
                            _cts.Token);
                        break;
                    case QualityOfService.AtLeastOnce:
                    case QualityOfService.ExactlyOnce:
                        await EnsureStreamAsync(_cts.Token);
                        var ack = await provider.ExecuteTransportPublishAsync(ct =>
                            _js!.PublishAsync(subject: Subject, data: Payload,
                                opts: new NatsJSPubOpts { MsgId = Headers["MessageId"].ToString() }, headers: Headers, cancellationToken: ct),
                            _cts.Token);
                        CatgaLog.NatsBatchPublishedJetStream(logger, ack.Seq, ack.Duplicate);
                        batchSpan?.AddActivityEvent(CatgaActivitySource.Events.NatsBatchItemSent,
                            ("subject", Subject),
                            ("qos", (int)QoS),
                            ("seq", (long)ack.Seq),
                            ("dup", ack.Duplicate));
                        break;
                }
            }
            catch (Exception ex)
            {
                CatgaLog.NatsBatchPublishFailed(logger, ex, Subject);
                CatgaDiagnostics.MessagesFailed.Add(1,
                    new KeyValuePair<string, object?>("component", "Transport.NATS"),
                    new KeyValuePair<string, object?>("destination", Subject),
                    new KeyValuePair<string, object?>("reason", "batch_item"));
                batchSpan?.SetError(ex);
                batchSpan?.AddActivityEvent(CatgaActivitySource.Events.NatsBatchItemFailed,
                    ("subject", Subject));
            }
        }
    }

    private async Task EnsureStreamAsync(CancellationToken cancellationToken)
    {
        if (_jsReady) return;
        lock (_jsLock)
        {
            if (_jsReady) return;
            _jsReady = true;
        }
        var name = ($"{_prefix}_STREAM").ToUpperInvariant();
        var subjects = new[] { $"{_prefix}.>" };
        var cfg = new StreamConfig(name, subjects: subjects);
        try
        {
            await _js!.CreateStreamAsync(cfg, cancellationToken);
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 400)
        {
            // stream exists, ignore
        }
    }

    public async Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(IEnumerable<TMessage> messages, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
    {
        await BatchOperationHelper.ExecuteBatchAsync(
            messages,
            m => PublishAsync(m, context, cancellationToken));
    }

    public Task SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(IEnumerable<TMessage> messages, string destination, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
        => PublishBatchAsync(messages, context, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string GetSubjectCached<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>()
        => SubjectCache<TMessage>.Subject ??= BuildSubject<TMessage>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string BuildSubject<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>()
    {
        var name = _naming != null ? _naming(typeof(TMessage)) : TypeNameCache<TMessage>.Name;
        return $"{_prefix}.{name}";
    }
}

/// <summary>Zero-allocation subject cache per message type</summary>
internal static class SubjectCache<T>
{
    public static string? Subject;
}
