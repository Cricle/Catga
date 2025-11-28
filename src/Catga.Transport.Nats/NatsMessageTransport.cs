using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Catga.Abstractions;
using Catga.Core;
using Catga.Observability;
using Catga.Resilience;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Catga.Configuration;

namespace Catga.Transport.Nats;

/// <summary>NATS transport - delegates QoS to NATS native capabilities (Core Pub/Sub + JetStream)</summary>
/// <remarks>
/// QoS Mapping:
/// - QoS 0 (AtMostOnce): NATS Core Pub/Sub (fire-and-forget)
/// - QoS 1 (AtLeastOnce): JetStream with Ack (guaranteed delivery, may duplicate)
/// - QoS 2 (ExactlyOnce): JetStream with MsgId deduplication (exactly-once using NATS native dedup)
///
/// Catga responsibilities (via Pipeline Behaviors):
/// - Application-level idempotency (business logic dedup)
/// - Retry logic (with exponential backoff)
/// - Outbox/Inbox pattern (transactional outbox)
/// - Validation, caching, logging, tracing
/// </remarks>
public class NatsMessageTransport : IMessageTransport, IAsyncDisposable
{
    private readonly INatsConnection _connection;
    private readonly IMessageSerializer _serializer;
    private readonly ILogger<NatsMessageTransport> _logger;
    private readonly string _subjectPrefix;
    private Func<Type, string>? _naming;
    private INatsJSContext? _jsContext;
    private readonly IResiliencePipelineProvider _provider;
    private readonly ConcurrentDictionary<string, Task> _subscriptions = new();
    private volatile bool _jsStreamEnsured;
    private readonly object _jsInitLock = new();
    private readonly ConcurrentDictionary<long, long> _dedupCache = new();
    private readonly ConcurrentQueue<long> _dedupOrder = new();
    private const int _dedupCapacity = 10000;
    private static readonly TimeSpan _dedupTtl = TimeSpan.FromMinutes(2);
    private readonly CancellationTokenSource _cts = new();

    // Optional auto-batching state
    private readonly NatsTransportOptions? _options;
    private readonly ConcurrentQueue<(string Subject, byte[] Payload, NatsHeaders Headers, QualityOfService QoS)> _batchQueue = new();
    private int _batchQueueCount;
    private Timer? _flushTimer;
    private readonly object _flushLock = new();

    public string Name => "NATS";
    public BatchTransportOptions? BatchOptions => null;
    public CompressionTransportOptions? CompressionOptions => null;

    public NatsMessageTransport(INatsConnection connection, IMessageSerializer serializer, ILogger<NatsMessageTransport> logger, NatsTransportOptions? options = null, IResiliencePipelineProvider? provider = null)
    {
        _connection = connection;
        _serializer = serializer;
        _logger = logger;
        // Normalize prefix to avoid double dots when composing subject
        _subjectPrefix = (options?.SubjectPrefix ?? "catga").TrimEnd('.');
        _naming = options?.Naming;
        _jsContext = new NatsJSContext(_connection);
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _options = options;

        // Initialize periodic flush timer only if batching is configured
        if (_options?.Batch is { EnableAutoBatching: true } batch)
        {
            _flushTimer = new Timer(static state =>
            {
                var self = (NatsMessageTransport)state!;
                try { self.TryFlushBatchTimer(); } catch { /* swallow */ }
            }, this, batch.BatchTimeout, batch.BatchTimeout);
        }
    }

    private void CleanupDedup(long nowTicks)
    {
        var evicted = 0;
        foreach (var kv in _dedupCache)
        {
            if (nowTicks - kv.Value > _dedupTtl.Ticks)
            {
                if (_dedupCache.TryRemove(kv.Key, out _)) evicted++;
            }
        }
        if (evicted > 0) CatgaDiagnostics.NatsDedupEvictions.Add(evicted);
    }

    private void EvictIfNeeded()
    {
        var evicted = 0;
        while (_dedupCache.Count > _dedupCapacity && _dedupOrder.TryDequeue(out var old))
        {
            if (_dedupCache.TryRemove(old, out _)) evicted++;
        }
        if (evicted > 0) CatgaDiagnostics.NatsDedupEvictions.Add(evicted);
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts.Cancel(); } catch { }
        if (!_subscriptions.IsEmpty)
        {
            try { await Task.WhenAll(_subscriptions.Values); } catch { /* ignore */ }
            _subscriptions.Clear();
        }
        _flushTimer?.Dispose();
        _cts.Dispose();
    }

    public NatsMessageTransport(INatsConnection connection, IMessageSerializer serializer, ILogger<NatsMessageTransport> logger, CatgaOptions globalOptions, NatsTransportOptions? options = null, IResiliencePipelineProvider? provider = null)
        : this(connection, serializer, logger, options, provider)
    {
        if (_naming == null)
            _naming = globalOptions?.EndpointNamingConvention;
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
            activity.SetTag(CatgaActivitySource.Tags.MessageType, TypeNameCache<TMessage>.Name);
            activity.SetTag(CatgaActivitySource.Tags.MessageId, ctx.MessageId);
            var qosString = qos switch { QualityOfService.AtMostOnce => "AtMostOnce", QualityOfService.AtLeastOnce => "AtLeastOnce", QualityOfService.ExactlyOnce => "ExactlyOnce", _ => "Unknown" };
            activity.SetTag(CatgaActivitySource.Tags.QoS, qosString);
        }

        var payload = _serializer.Serialize(message!, typeof(TMessage));
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
        if (_options?.Batch is { EnableAutoBatching: true } batchOptions)
        {
            Enqueue(subject, payload, headers, qos, batchOptions);
            return; // queued for background flush
        }

        try
        {
            // Delegate QoS handling to NATS native capabilities
            switch (qos)
            {
                case QualityOfService.AtMostOnce:
                    // NATS Core Pub/Sub: fire-and-forget, no ack, no persistence
                    await _provider.ExecuteTransportPublishAsync(ct =>
                        _connection.PublishAsync(subject, payload, headers: headers, cancellationToken: ct),
                        cancellationToken);
                    CatgaLog.NatsPublishedCore(_logger, ctx.MessageId);
                    break;

                case QualityOfService.AtLeastOnce:
                    await EnsureStreamAsync(cancellationToken);
                    // JetStream: guaranteed delivery, may duplicate (consumer acks)
                    var ack1 = await _provider.ExecuteTransportPublishAsync(ct =>
                        _jsContext!.PublishAsync(subject: subject, data: payload, opts: new NatsJSPubOpts { MsgId = ctx.MessageId?.ToString() }, headers: headers, cancellationToken: ct),
                        cancellationToken);
                    CatgaLog.NatsPublishedQoS1(_logger, ctx.MessageId, ack1.Seq, ack1.Duplicate);
                    break;

                case QualityOfService.ExactlyOnce:
                    await EnsureStreamAsync(cancellationToken);
                    // JetStream with MsgId deduplication: exactly-once using NATS native dedup window (default 2 minutes)
                    // Note: Application-level idempotency (via IdempotencyBehavior) provides additional business logic dedup
                    var ack2 = await _provider.ExecuteTransportPublishAsync(ct =>
                        _jsContext!.PublishAsync(subject: subject, data: payload, opts: new NatsJSPubOpts { MsgId = ctx.MessageId?.ToString() }, headers: headers, cancellationToken: ct),
                        cancellationToken);
                    CatgaLog.NatsPublishedQoS2(_logger, ctx.MessageId, ack2.Seq, ack2.Duplicate);
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
            CatgaLog.NatsPublishFailed(_logger, ex, subject, ctx.MessageId);
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
            await foreach (var msg in _connection.SubscribeAsync<byte[]>(subject, cancellationToken: ct))
            {
                try
                {
                    // Try to restore parent from W3C headers if present
                    Activity? activity;
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
                    }
                    if (msg.Data == null || msg.Data.Length == 0)
                    {
                        CatgaLog.NatsEmptyMessage(_logger, subject);
                        if (ObservabilityHooks.IsEnabled)
                        {
                            CatgaDiagnostics.MessagesFailed.Add(1,
                                new KeyValuePair<string, object?>("component", "Transport.NATS"),
                                new KeyValuePair<string, object?>("destination", subject),
                                new KeyValuePair<string, object?>("reason", "empty"));
                        }
                        continue;
                    }
                    var message = (TMessage?)_serializer.Deserialize(msg.Data, typeof(TMessage));
                    if (activity != null)
                    {
                        var headerType = msg.Headers?["MessageType"].ToString();
                        if (!string.IsNullOrEmpty(headerType)) activity.SetTag(CatgaActivitySource.Tags.MessageType, headerType);
                    }
                    if (message == null)
                    {
                        CatgaLog.NatsDeserializeFailed(_logger, subject);
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
                        if (_dedupCache.TryGetValue(messageId.Value, out var ts))
                        {
                            if (now - ts <= _dedupTtl.Ticks)
                            {
                                CatgaLog.NatsDroppedDuplicate(_logger, messageId, qosHeader, subject);
                                CatgaDiagnostics.NatsDedupDrops.Add(1);
                                continue;
                            }
                            _dedupCache[messageId.Value] = now;
                            _dedupOrder.Enqueue(messageId.Value);
                        }
                        else
                        {
                            if (_dedupCache.TryAdd(messageId.Value, now))
                                _dedupOrder.Enqueue(messageId.Value);
                        }
                        if (_dedupCache.Count > _dedupCapacity)
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
                    await handler(message, context);
                }
                catch (Exception ex) { CatgaLog.NatsProcessingError(_logger, ex, subject); }
            }
        }, cancellationToken);
        _subscriptions[subject] = task;
        return Task.CompletedTask;
    }

    private void Enqueue(string subject, byte[] payload, NatsHeaders headers, QualityOfService qos, BatchTransportOptions batch)
    {
        // Backpressure: drop oldest when exceeding MaxQueueLength
        var newCount = Interlocked.Increment(ref _batchQueueCount);
        if (_options!.MaxQueueLength > 0 && newCount > _options.MaxQueueLength)
        {
            while (Interlocked.CompareExchange(ref _batchQueueCount, _batchQueueCount, _batchQueueCount) > _options.MaxQueueLength && _batchQueue.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _batchQueueCount);
            }
        }
        _batchQueue.Enqueue((subject, payload, headers, qos));

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
        var batch = _options?.Batch;
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
        int toProcess = Math.Min(_batchQueueCount, batch.MaxBatchSize);
        if (toProcess <= 0) return;

        var list = new List<(string Subject, byte[] Payload, NatsHeaders Headers, QualityOfService QoS)>(toProcess);
        while (toProcess-- > 0 && _batchQueue.TryDequeue(out var item))
        {
            Interlocked.Decrement(ref _batchQueueCount);
            list.Add(item);
        }

        foreach (var item in list)
        {
            try
            {
                switch (item.QoS)
                {
                    case QualityOfService.AtMostOnce:
                        await _provider.ExecuteTransportPublishAsync(ct =>
                            _connection.PublishAsync(item.Subject, item.Payload, headers: item.Headers, cancellationToken: ct),
                            _cts.Token);
                        break;
                    case QualityOfService.AtLeastOnce:
                    case QualityOfService.ExactlyOnce:
                        await EnsureStreamAsync(_cts.Token);
                        var ack = await _provider.ExecuteTransportPublishAsync(ct =>
                            _jsContext!.PublishAsync(subject: item.Subject, data: item.Payload,
                                opts: new NatsJSPubOpts { MsgId = item.Headers["MessageId"].ToString() }, headers: item.Headers, cancellationToken: ct),
                            _cts.Token);
                        CatgaLog.NatsBatchPublishedJetStream(_logger, ack.Seq, ack.Duplicate);
                        break;
                }
            }
            catch (Exception ex)
            {
                CatgaLog.NatsBatchPublishFailed(_logger, ex, item.Subject);
                CatgaDiagnostics.MessagesFailed.Add(1,
                    new KeyValuePair<string, object?>("component", "Transport.NATS"),
                    new KeyValuePair<string, object?>("destination", item.Subject),
                    new KeyValuePair<string, object?>("reason", "batch_item"));
            }
        }
    }

    private async Task EnsureStreamAsync(CancellationToken cancellationToken)
    {
        if (_jsStreamEnsured) return;
        lock (_jsInitLock)
        {
            if (_jsStreamEnsured) return;
            _jsStreamEnsured = true;
        }
        var name = ($"{_subjectPrefix}_STREAM").ToUpperInvariant();
        var subjects = new[] { $"{_subjectPrefix}.>" };
        var cfg = new StreamConfig(name, subjects: subjects);
        try
        {
            await _jsContext!.CreateStreamAsync(cfg, cancellationToken);
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
        return $"{_subjectPrefix}.{name}";
    }
}

/// <summary>Zero-allocation subject cache per message type</summary>
internal static class SubjectCache<T>
{
    public static string? Subject;
}
