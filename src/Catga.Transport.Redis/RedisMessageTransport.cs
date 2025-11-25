using System;
using Catga.Abstractions;
using Catga.Core;
using Catga.Transport;
using StackExchange.Redis;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Generic;
using Catga.Observability;
using Catga.Resilience;

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

        using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Publish", ActivityKind.Producer);
        if (activity != null)
        {
            activity.SetTag(CatgaActivitySource.Tags.MessagingSystem, "redis");
            activity.SetTag(CatgaActivitySource.Tags.MessagingDestination, subject);
            activity.SetTag(CatgaActivitySource.Tags.MessageType, TypeNameCache<TMessage>.Name);
        }

        // Metrics tags
#if NET8_0_OR_GREATER
        var tag_component = new KeyValuePair<string, object?>("component", "Transport.Redis");
        var tag_type = new KeyValuePair<string, object?>("message_type", TypeNameCache<TMessage>.Name);
        var tag_dest = new KeyValuePair<string, object?>("destination", subject);
#else
        var tags_pub = new TagList { { "component", "Transport.Redis" }, { "message_type", TypeNameCache<TMessage>.Name }, { "destination", subject } };
#endif

        try
        {
            // Always use Pub/Sub for broadcast messages
            await _provider.ExecuteTransportPublishAsync(ct => new ValueTask(
                _subscriber.PublishAsync(
                    RedisChannel.Literal(subject),
                    payload,
                    CommandFlags.FireAndForget)),
                cancellationToken);
            #if NET8_0_OR_GREATER
            CatgaDiagnostics.MessagesPublished.Add(1, tag_component, tag_type, tag_dest);
            #else
            CatgaDiagnostics.MessagesPublished.Add(1, tags_pub);
            #endif
        }
        catch (Exception)
        {
#if NET8_0_OR_GREATER
            CatgaDiagnostics.MessagesFailed.Add(1,
                new KeyValuePair<string, object?>("component", "Transport.Redis"),
                new KeyValuePair<string, object?>("destination", subject));
#else
            var fail = new TagList { { "component", "Transport.Redis" }, { "destination", subject } };
            CatgaDiagnostics.MessagesFailed.Add(1, fail);
#endif
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

        #if NET8_0_OR_GREATER
        var tag_component = new KeyValuePair<string, object?>("component", "Transport.Redis");
        var tag_type = new KeyValuePair<string, object?>("message_type", TypeNameCache<TMessage>.Name);
        var tag_dest = new KeyValuePair<string, object?>("destination", streamKey);
        #else
        var tags_stream = new TagList { { "component", "Transport.Redis" }, { "message_type", TypeNameCache<TMessage>.Name }, { "destination", streamKey } };
        #endif

        try
        {
            // Use Streams for point-to-point messaging
            await _provider.ExecuteTransportSendAsync(ct => new ValueTask(
                _db.StreamAddAsync(
                    streamKey,
                    "data",
                    payload,
                    flags: CommandFlags.DemandMaster)),
                cancellationToken);
            #if NET8_0_OR_GREATER
            CatgaDiagnostics.MessagesPublished.Add(1, tag_component, tag_type, tag_dest);
            #else
            CatgaDiagnostics.MessagesPublished.Add(1, tags_stream);
            #endif
        }
        catch (Exception)
        {
#if NET8_0_OR_GREATER
            CatgaDiagnostics.MessagesFailed.Add(1,
                new KeyValuePair<string, object?>("component", "Transport.Redis"),
                new KeyValuePair<string, object?>("destination", streamKey));
#else
            var fail2 = new TagList { { "component", "Transport.Redis" }, { "destination", streamKey } };
            CatgaDiagnostics.MessagesFailed.Add(1, fail2);
#endif
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
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Receive", ActivityKind.Consumer);
            if (activity != null)
            {
                activity.SetTag(CatgaActivitySource.Tags.MessagingSystem, "redis");
                activity.SetTag(CatgaActivitySource.Tags.MessagingDestination, subject);
                activity.SetTag(CatgaActivitySource.Tags.MessageType, TypeNameCache<TMessage>.Name);
            }
            try
            {
                var (message, ctx) = DeserializeMessage<TMessage>(channelMessage.Message!);
                await handler(message, ctx ?? new TransportContext());
            }
            catch (Exception ex)
            {
                activity?.SetError(ex);
                // Log error and count failure
                #if NET8_0_OR_GREATER
                CatgaDiagnostics.MessagesFailed.Add(1,
                    new KeyValuePair<string, object?>("component", "Transport.Redis"),
                    new KeyValuePair<string, object?>("destination", subject));
                #else
                var failTags = new TagList { { "component", "Transport.Redis" }, { "destination", subject } };
                CatgaDiagnostics.MessagesFailed.Add(1, failTags);
                #endif
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

        _cts.Dispose();
    }
}
