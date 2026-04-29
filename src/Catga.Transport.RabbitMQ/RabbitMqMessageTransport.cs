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
using Catga.Transport;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Catga.Transport.RabbitMQ;

/// <summary>
/// RabbitMQ transport for Catga. Supports topic exchange, routing keys,
/// durable queues, and native request/reply via correlation ID.
/// </summary>
public sealed class RabbitMqMessageTransport : IMessageTransport, IRequestTimeoutDefaults, IAsyncInitializable, IStoppable, IWaitable, IHealthCheckable, IAsyncDisposable
{
    private const string MetadataHeaderPrefix = "meta.";
    private const string SentAtHeader = "sent_at";
    private const string PriorityHeader = "x-priority";
    private readonly IMessageSerializer _serializer;
    private readonly IResiliencePipelineProvider _resilience;
    private readonly RabbitMqTransportOptions _options;
    private readonly ILogger<RabbitMqMessageTransport>? _logger;
    private readonly string _prefix;

    private IConnection? _connection;
    private IChannel? _publishChannel;
    private volatile bool _acceptingMessages = true;
    private int _pendingOperations;
    private volatile bool _isHealthy;
    private DateTimeOffset? _lastHealthCheck;

    // Pending RPC replies: correlationId -> TCS<byte[]>
    private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _pendingReplies = new();

    public RabbitMqMessageTransport(
        IMessageSerializer serializer,
        IResiliencePipelineProvider resilience,
        RabbitMqTransportOptions? options = null,
        ILogger<RabbitMqMessageTransport>? logger = null)
    {
        _serializer = serializer;
        _resilience = resilience;
        _options = options ?? new RabbitMqTransportOptions();
        _logger = logger;
        _prefix = NormalizePrefix(_options.Prefix);
    }

    public string Name => "RabbitMQ";
    public BatchTransportOptions? BatchOptions => null;
    public CompressionTransportOptions? CompressionOptions => null;
    public TimeSpan DefaultRequestTimeout => _options.RequestTimeout;
    public bool IsAcceptingMessages => _acceptingMessages;
    public int PendingOperations => _pendingOperations;
    public bool IsHealthy => _isHealthy;
    public string? HealthStatus => _isHealthy ? "Connected" : "Disconnected";
    public DateTimeOffset? LastHealthCheck => _lastHealthCheck;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var factory = new ConnectionFactory { Uri = new Uri(_options.Uri) };
            _connection = await factory.CreateConnectionAsync(cancellationToken);
            _publishChannel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

            if (_options.DeclareExchange)
            {
                await _publishChannel.ExchangeDeclareAsync(
                    _options.Exchange,
                    RabbitMqTransportDelay.ResolveExchangeType(_options),
                    durable: _options.DurableExchange,
                    autoDelete: false,
                    arguments: RabbitMqTransportDelay.BuildExchangeArguments(_options),
                    cancellationToken: cancellationToken);
            }

            _isHealthy = true;
            _lastHealthCheck = DateTimeOffset.UtcNow;
            _logger?.LogInformation("[RabbitMQ] Connected to {Uri}, exchange: {Exchange}", _options.Uri, _options.Exchange);
        }
        catch
        {
            _isHealthy = false;
            _lastHealthCheck = DateTimeOffset.UtcNow;
            throw;
        }
    }

    public void StopAcceptingMessages() => _acceptingMessages = false;

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
                break;
            }
        }
    }

    // ── Publish ──────────────────────────────────────────────────────────────

    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
        => await PublishCoreAsync(message, GetRoutingKey<TMessage>(), context, cancellationToken);

    public Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        string destination,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
        => PublishCoreAsync(message, ResolveRoutingKey<TMessage>(destination), context, cancellationToken);

    // ── Subscribe ────────────────────────────────────────────────────────────

    public Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken cancellationToken = default)
        where TMessage : class
        => SubscribeCoreAsync(GetQueueName<TMessage>(), GetRoutingKey<TMessage>(), handler, cancellationToken);

    public Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        string destination,
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken cancellationToken = default)
        where TMessage : class
        => SubscribeCoreAsync(ResolveQueueName<TMessage>(destination), ResolveRoutingKey<TMessage>(destination), handler, cancellationToken);

    // ── Batch ────────────────────────────────────────────────────────────────

    public Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        IEnumerable<TMessage> messages,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
        => PublishBatchCoreAsync(messages, GetRoutingKey<TMessage>(), context, cancellationToken);

    public Task SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        IEnumerable<TMessage> messages,
        string destination,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
        => PublishBatchCoreAsync(messages, ResolveRoutingKey<TMessage>(destination), context, cancellationToken);

    // ── Publish helpers ──────────────────────────────────────────────────────

    private async Task PublishCoreAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        string routingKey,
        TransportContext? context,
        CancellationToken cancellationToken)
        where TMessage : class
    {
        EnsureReady();
        Interlocked.Increment(ref _pendingOperations);
        try
        {
            var effectiveContext = TransportMessageContextAccessor.EnrichOutgoing(message, GetEffectiveContext(message, context));
            TransportMessageContextAccessor.ApplyToMessage(message, effectiveContext);
            var body = _serializer.Serialize(message);
            using var activity = StartPublishActivity<TMessage>(routingKey, effectiveContext);
            var props = BuildProperties(message, effectiveContext);

            await _resilience.ExecuteTransportPublishAsync(async ct =>
            {
                await _publishChannel!.BasicPublishAsync(
                    _options.Exchange, routingKey, false, props, body, ct);
                if (effectiveContext.Metadata is { Count: > 0 } metadata &&
                    metadata.TryGetValue("reply_to", out var replyTo) &&
                    !string.IsNullOrWhiteSpace(replyTo))
                {
                    await _publishChannel.BasicPublishAsync(
                        string.Empty,
                        replyTo,
                        false,
                        props,
                        body,
                        ct);
                }
            }, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _pendingOperations);
        }
    }

    private async Task PublishBatchCoreAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        IEnumerable<TMessage> messages,
        string routingKey,
        TransportContext? context,
        CancellationToken cancellationToken)
        where TMessage : class
    {
        EnsureReady();
        Interlocked.Increment(ref _pendingOperations);
        try
        {
            foreach (var message in messages)
            {
                var effectiveContext = TransportMessageContextAccessor.EnrichOutgoing(message, GetEffectiveContext(message, context));
                TransportMessageContextAccessor.ApplyToMessage(message, effectiveContext);
                var body = _serializer.Serialize(message);
                using var activity = StartPublishActivity<TMessage>(routingKey, effectiveContext);
                var props = BuildProperties(message, effectiveContext);
                await _resilience.ExecuteTransportPublishAsync(async ct =>
                {
                    await _publishChannel!.BasicPublishAsync(
                        _options.Exchange, routingKey, false, props, body, ct);
                    if (effectiveContext.Metadata is { Count: > 0 } metadata &&
                        metadata.TryGetValue("reply_to", out var replyTo) &&
                        !string.IsNullOrWhiteSpace(replyTo))
                    {
                        await _publishChannel.BasicPublishAsync(
                            string.Empty,
                            replyTo,
                            false,
                            props,
                            body,
                            ct);
                    }
                }, cancellationToken);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _pendingOperations);
        }
    }

    private async Task SubscribeCoreAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        string queueName,
        string routingKey,
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken cancellationToken)
        where TMessage : class
    {
        EnsureReady();

        var channel = await _connection!.CreateChannelAsync(cancellationToken: cancellationToken);
        var queueArguments = RabbitMqTransportPriority.BuildQueueArguments(_options);
        await channel.QueueDeclareAsync(queueName,
            durable: _options.DurableQueues,
            exclusive: false,
            autoDelete: _options.AutoDeleteQueues,
            arguments: queueArguments,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(queueName, _options.Exchange, routingKey, cancellationToken: cancellationToken);
        await channel.BasicQosAsync(0, _options.PrefetchCount, false, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            Interlocked.Increment(ref _pendingOperations);
            using var activity = StartReceiveActivity<TMessage>(queueName, ea.BasicProperties);
            try
            {
                var msg = _serializer.Deserialize<TMessage>(ea.Body.ToArray());
                var ctx = BuildContext(ea.BasicProperties);
                using var scope = TransportMessageContextAccessor.Push(ctx);
                await handler(msg, ctx);
                await channel.BasicAckAsync(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                activity?.SetError(ex);
                _logger?.LogError(ex, "[RabbitMQ] Handler failed for {Queue}", queueName);
                await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: true);
            }
            finally
            {
                Interlocked.Decrement(ref _pendingOperations);
            }
        };

        await channel.BasicConsumeAsync(queueName, autoAck: false, consumer, cancellationToken);
        _logger?.LogInformation("[RabbitMQ] Subscribed to {Queue} (routing: {Key})", queueName, routingKey);
    }

    // ── Request/Reply ────────────────────────────────────────────────────────

    /// <summary>
    /// RabbitMQ native request/reply using correlation ID and reply-to queue.
    /// </summary>
    public async Task<TResponse?> RequestAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(
        TMessage message,
        string destination,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        where TMessage : class
        where TResponse : class
    {
        EnsureReady();
        Interlocked.Increment(ref _pendingOperations);
        try
        {
        var correlationId = MessageExtensions.NewCorrelationId();
        var correlationKey = correlationId.ToString();
        var effectiveContext = TransportMessageContextAccessor.EnrichOutgoing(message, new TransportContext
        {
            MessageId = MessageExtensions.NewMessageId(),
            CorrelationId = correlationId,
            MessageType = TypeNameCache<TMessage>.FullName,
            SentAt = DateTime.UtcNow
        });
        TransportMessageContextAccessor.ApplyToMessage(message, effectiveContext);

        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingReplies[correlationKey] = tcs;

        // Create a temporary reply queue
        var replyChannel = await _connection!.CreateChannelAsync(cancellationToken: cancellationToken);
        var replyQueue = await replyChannel.QueueDeclareAsync(
            queue: "", durable: false, exclusive: true, autoDelete: true,
            cancellationToken: cancellationToken);

        var replyConsumer = new AsyncEventingBasicConsumer(replyChannel);
        replyConsumer.ReceivedAsync += (_, ea) =>
        {
            if (ea.BasicProperties.CorrelationId == correlationKey &&
                _pendingReplies.TryRemove(correlationKey, out var pending))
            {
                pending.TrySetResult(ea.Body.ToArray());
            }
            return Task.CompletedTask;
        };
        await replyChannel.BasicConsumeAsync(replyQueue.QueueName, autoAck: true, replyConsumer, cancellationToken);

        // Publish request with reply-to
        var body = _serializer.Serialize(message);
        var routingKey = ResolveRoutingKey<TMessage>(destination);
        using var activity = StartPublishActivity<TMessage>(routingKey, effectiveContext);
        var props = BuildProperties(message, effectiveContext);
        props.CorrelationId = correlationKey;
        props.ReplyTo = replyQueue.QueueName;
        await _resilience.ExecuteTransportPublishAsync(
            ct => _publishChannel!.BasicPublishAsync(
                _options.Exchange, routingKey, false, props, body, ct),
            cancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        cts.Token.Register(() =>
        {
            if (_pendingReplies.TryRemove(correlationKey, out var pending))
                pending.TrySetCanceled();
        });

        try
        {
            var responseBytes = await tcs.Task;
            return _serializer.Deserialize<TResponse>(responseBytes);
        }
        catch (OperationCanceledException)
        {
            _pendingReplies.TryRemove(correlationKey, out _);
            return null;
        }
        finally
        {
            await replyChannel.DisposeAsync();
        }
        }
        finally
        {
            Interlocked.Decrement(ref _pendingOperations);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string GetRoutingKey<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>()
        => $"{_prefix}{GetEndpointName<TMessage>()}";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string GetQueueName<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>()
        => $"{_prefix}{GetEndpointName<TMessage>()}";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string GetEndpointName<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>()
        => _options.EndpointNaming != null
            ? _options.EndpointNaming(typeof(TMessage))
            : TypeNameCache<TMessage>.Name;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string ResolveRoutingKey<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(string destination)
        => string.IsNullOrWhiteSpace(destination)
            ? GetRoutingKey<TMessage>()
            : destination.StartsWith(_prefix, StringComparison.Ordinal)
                ? destination
                : $"{_prefix}{destination.TrimStart('.')}";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string ResolveQueueName<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(string destination)
        => string.IsNullOrWhiteSpace(destination)
            ? GetQueueName<TMessage>()
            : destination.StartsWith(_prefix, StringComparison.Ordinal)
                ? destination
                : $"{_prefix}{destination.TrimStart('.')}";

    private static string NormalizePrefix(string? prefix)
    {
        var effective = string.IsNullOrWhiteSpace(prefix) ? "catga." : prefix.Trim();
        return effective.EndsWith(".", StringComparison.Ordinal) ? effective : $"{effective}.";
    }

    private BasicProperties BuildProperties(object message, TransportContext context)
    {
        var props = new BasicProperties
        {
            ContentType = "application/octet-stream",
            DeliveryMode = DeliveryModes.Persistent
        };
        if (context.MessageId is long messageId)
            props.MessageId = messageId.ToString();
        if (context.CorrelationId is long correlationId)
            props.CorrelationId = correlationId.ToString();
        if (!string.IsNullOrWhiteSpace(context.MessageType))
            props.Type = context.MessageType;
        if (context.SentAt.HasValue)
            props.Timestamp = new AmqpTimestamp(new DateTimeOffset(context.SentAt.Value).ToUnixTimeSeconds());
        if (_options.MessageTtlMs.HasValue)
            props.Expiration = _options.MessageTtlMs.Value.ToString();
        if (RabbitMqTransportPriority.ResolvePriority(context, _options) is byte priority)
            props.Priority = priority;

        var headers = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (RabbitMqTransportDelay.ResolveDelayMilliseconds(message, context) is int delayMs)
            headers[RabbitMqTransportDelay.DelayHeaderKey] = delayMs;
        if (context.SentAt.HasValue)
            headers[SentAtHeader] = context.SentAt.Value.ToString("O");

        if (context.Metadata is { Count: > 0 })
        {
            foreach (var (key, value) in context.Metadata)
                headers[$"{MetadataHeaderPrefix}{key}"] = value;
        }

        var current = Activity.Current;
        if (ObservabilityHooks.IsEnabled && current != null)
        {
            headers["traceparent"] = current.Id;
            if (!string.IsNullOrWhiteSpace(current.TraceStateString))
                headers["tracestate"] = current.TraceStateString;
        }

        if (headers.Count > 0)
            props.Headers = headers;

        return props;
    }

    private static TransportContext BuildContext(IReadOnlyBasicProperties props)
    {
        long? messageId = long.TryParse(props.MessageId, out var parsedMessageId) && parsedMessageId != 0
            ? parsedMessageId
            : null;
        long? correlationId = long.TryParse(props.CorrelationId, out var parsedCorrelationId) && parsedCorrelationId != 0
            ? parsedCorrelationId
            : null;
        var messageType = props.Type;
        DateTime? sentAt = null;
        Dictionary<string, string>? metadata = null;

        if (props.Headers is { Count: > 0 } headers)
        {
            foreach (var (key, rawValue) in headers)
            {
                var value = DecodeHeaderValue(rawValue);
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                if (!messageId.HasValue &&
                    MatchesHeaderKey(key, "messageid") &&
                    long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedMessageId) &&
                    parsedMessageId != 0)
                {
                    messageId = parsedMessageId;
                    continue;
                }

                if (!correlationId.HasValue &&
                    MatchesHeaderKey(key, "correlationid") &&
                    long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedCorrelationId) &&
                    parsedCorrelationId != 0)
                {
                    correlationId = parsedCorrelationId;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(messageType) && MatchesHeaderKey(key, "messagetype"))
                {
                    messageType = value;
                    continue;
                }

                if (MatchesHeaderKey(key, "sentat") &&
                    DateTime.TryParse(
                        value,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var parsedSentAt))
                {
                    sentAt = parsedSentAt;
                    continue;
                }

                if (TryGetMetadataKey(key, out var metadataKey))
                {
                    metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
                    metadata[metadataKey] = value;
                }
            }
        }

        if (!sentAt.HasValue && props.Timestamp.UnixTime > 0)
            sentAt = DateTimeOffset.FromUnixTimeSeconds(props.Timestamp.UnixTime).UtcDateTime;

        if (props.Priority > 0)
        {
            metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
            metadata.TryAdd(PriorityHeader, props.Priority.ToString(CultureInfo.InvariantCulture));
        }

        if (props.Headers is { Count: > 0 } rawHeaders)
        {
            if (TryGetHeaderCaseInsensitive(rawHeaders, RabbitMqTransportDelay.DelayHeaderKey, out var rawDelay) &&
                TryDecodePositiveInt(rawDelay, out var delayMs))
            {
                metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
                metadata.TryAdd(RabbitMqTransportDelay.DelayHeaderKey, delayMs.ToString(CultureInfo.InvariantCulture));
            }

            if (TryGetHeaderCaseInsensitive(rawHeaders, PriorityHeader, out var rawPriority) &&
                TryDecodeNonNegativeByte(rawPriority, out var priority))
            {
                metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
                metadata.TryAdd(PriorityHeader, priority.ToString(CultureInfo.InvariantCulture));
            }
        }

        if (!string.IsNullOrWhiteSpace(props.ReplyTo))
        {
            metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
            metadata["reply_to"] = props.ReplyTo!;
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

    private static string? DecodeHeaderValue(object? value)
        => value switch
        {
            null => null,
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            ReadOnlyMemory<byte> memory => Encoding.UTF8.GetString(memory.Span),
            _ => value.ToString()
        };

    private static bool TryGetHeaderCaseInsensitive(
        IDictionary<string, object?> headers,
        string key,
        out object? value)
    {
        if (headers.TryGetValue(key, out value))
            return true;

        foreach (var entry in headers)
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

    private static bool MatchesHeaderKey(string key, string normalizedExpected)
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

    private static bool TryDecodePositiveInt(object? value, out int decoded)
    {
        decoded = 0;
        return value switch
        {
            byte b when b > 0 => (decoded = b) > 0,
            sbyte sb when sb > 0 => (decoded = sb) > 0,
            short s when s > 0 => (decoded = s) > 0,
            ushort us when us > 0 => (decoded = us) > 0,
            int i when i > 0 => (decoded = i) > 0,
            uint ui when ui is > 0 and <= int.MaxValue => (decoded = (int)ui) > 0,
            long l when l is > 0 and <= int.MaxValue => (decoded = (int)l) > 0,
            ulong ul when ul is > 0 and <= int.MaxValue => (decoded = (int)ul) > 0,
            _ => int.TryParse(DecodeHeaderValue(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out decoded) && decoded > 0
        };
    }

    private static bool TryDecodeNonNegativeByte(object? value, out byte decoded)
    {
        decoded = 0;
        return value switch
        {
            byte b => AssignDecodedByte(b, out decoded),
            sbyte sb when sb >= 0 => AssignDecodedByte((byte)sb, out decoded),
            short s when s >= 0 && s <= byte.MaxValue => AssignDecodedByte((byte)s, out decoded),
            ushort us when us <= byte.MaxValue => AssignDecodedByte((byte)us, out decoded),
            int i when i >= 0 && i <= byte.MaxValue => AssignDecodedByte((byte)i, out decoded),
            uint ui when ui <= byte.MaxValue => AssignDecodedByte((byte)ui, out decoded),
            long l when l >= 0 && l <= byte.MaxValue => AssignDecodedByte((byte)l, out decoded),
            ulong ul when ul <= byte.MaxValue => AssignDecodedByte((byte)ul, out decoded),
            _ => byte.TryParse(DecodeHeaderValue(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out decoded)
        };
    }

    private static bool AssignDecodedByte(byte value, out byte decoded)
    {
        decoded = value;
        return true;
    }

    private static TransportContext GetEffectiveContext<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        TransportContext? context)
        where TMessage : class
    {
        var effective = context ?? default;

        if (!effective.MessageId.HasValue && message is IMessage metadata && metadata.MessageId != 0)
            effective = effective with { MessageId = metadata.MessageId };

        if (!effective.CorrelationId.HasValue && message is IMessage correlation && correlation.CorrelationId.HasValue)
            effective = effective with { CorrelationId = correlation.CorrelationId };

        if (string.IsNullOrWhiteSpace(effective.MessageType))
            effective = effective with { MessageType = TypeNameCache<TMessage>.FullName };

        if (!effective.SentAt.HasValue)
            effective = effective with { SentAt = DateTime.UtcNow };

        return effective;
    }

    private static Activity? StartPublishActivity<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        string routingKey,
        TransportContext context)
        where TMessage : class
    {
        if (!ObservabilityHooks.IsEnabled)
            return null;

        var activity = CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Publish", ActivityKind.Producer);
        if (activity == null)
            return null;

        activity.SetTag(CatgaActivitySource.Tags.MessagingSystem, "rabbitmq");
        activity.SetTag(CatgaActivitySource.Tags.MessagingDestination, routingKey);
        activity.SetTag(CatgaActivitySource.Tags.MessageType, context.MessageType ?? TypeNameCache<TMessage>.Name);
        if (context.MessageId.HasValue)
            activity.SetTag(CatgaActivitySource.Tags.MessageId, context.MessageId.Value.ToString());
        return activity;
    }

    private static Activity? StartReceiveActivity<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        string queueName,
        IReadOnlyBasicProperties props)
        where TMessage : class
    {
        if (!ObservabilityHooks.IsEnabled)
            return null;

        var traceParent = DecodeHeaderValue(props.Headers != null && TryGetHeaderCaseInsensitive(props.Headers, "traceparent", out var tp) ? tp : null);
        var traceState = DecodeHeaderValue(props.Headers != null && TryGetHeaderCaseInsensitive(props.Headers, "tracestate", out var ts) ? ts : null);

        Activity? activity;
        if (!string.IsNullOrWhiteSpace(traceParent))
        {
            try
            {
                var parent = ActivityContext.Parse(traceParent!, traceState);
                activity = CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Receive", ActivityKind.Consumer, parent);
            }
            catch
            {
                activity = CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Receive", ActivityKind.Consumer);
            }
        }
        else
        {
            activity = CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Receive", ActivityKind.Consumer);
        }

        if (activity == null)
            return null;

        var messageType = props.Type;
        var messageId = props.MessageId;

        if (props.Headers is { Count: > 0 } headers)
        {
            foreach (var (key, rawValue) in headers)
            {
                if (string.IsNullOrWhiteSpace(messageType) && MatchesHeaderKey(key, "messagetype"))
                    messageType = DecodeHeaderValue(rawValue);

                if (string.IsNullOrWhiteSpace(messageId) && MatchesHeaderKey(key, "messageid"))
                    messageId = DecodeHeaderValue(rawValue);

                if (!string.IsNullOrWhiteSpace(messageType) && !string.IsNullOrWhiteSpace(messageId))
                    break;
            }
        }

        activity.SetTag(CatgaActivitySource.Tags.MessagingSystem, "rabbitmq");
        activity.SetTag(CatgaActivitySource.Tags.MessagingDestination, queueName);
        activity.SetTag(CatgaActivitySource.Tags.MessageType, messageType ?? TypeNameCache<TMessage>.Name);
        if (!string.IsNullOrWhiteSpace(messageId))
            activity.SetTag(CatgaActivitySource.Tags.MessageId, messageId);
        return activity;
    }

    private void EnsureReady()
    {
        if (_publishChannel == null)
            throw new InvalidOperationException("RabbitMQ transport not initialized. Call InitializeAsync first.");
        if (!_acceptingMessages)
            throw new InvalidOperationException("Transport is shutting down.");
    }

    public async ValueTask DisposeAsync()
    {
        _acceptingMessages = false;
        _isHealthy = false;
        _lastHealthCheck = DateTimeOffset.UtcNow;
        await WaitForCompletionAsync();
        if (_publishChannel != null) await _publishChannel.DisposeAsync();
        if (_connection != null) await _connection.DisposeAsync();
    }
}
