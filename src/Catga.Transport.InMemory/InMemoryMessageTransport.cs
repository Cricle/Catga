using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Catga.Idempotency;
using Catga.Observability;
using Catga.Resilience;
using Catga.Configuration;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

using Polly;
#if NET8_0_OR_GREATER
using Polly.Retry;
#endif

namespace Catga.Transport;

/// <summary>In-memory message transport (for testing, supports QoS)</summary>
public class InMemoryMessageTransport : IMessageTransport
{
    private readonly InMemoryIdempotencyStore _idempotencyStore = new();
    private readonly IResiliencePipelineProvider _provider;
    private readonly ILogger<InMemoryMessageTransport>? _logger;
    private Func<Type, string>? _naming;

    public InMemoryMessageTransport(
        InMemoryTransportOptions? options = null,
        ILogger<InMemoryMessageTransport>? logger = null,
        IResiliencePipelineProvider? provider = null)
    {
        _logger = logger;
        options ??= new InMemoryTransportOptions();
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public InMemoryMessageTransport(
        InMemoryTransportOptions? options,
        ILogger<InMemoryMessageTransport>? logger,
        CatgaOptions globalOptions,
        IResiliencePipelineProvider provider)
        : this(options, logger, provider)
    {
        _naming = globalOptions?.EndpointNamingConvention;
    }

    public string Name => "InMemory";
    public BatchTransportOptions? BatchOptions => null;
    public CompressionTransportOptions? CompressionOptions => null;

    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
    {
        using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Publish", ActivityKind.Producer);
        var sw = Stopwatch.StartNew();

        // Get immutable snapshot (lock-free read via volatile)
        var handlers = TypedSubscribers<TMessage>.GetHandlers();
        if (handlers.Count == 0) return;

        var ctx = context ?? new TransportContext { MessageId = MessageExtensions.NewMessageId(), MessageType = TypeNameCache<TMessage>.FullName, SentAt = DateTime.UtcNow };
        var msg = message as IMessage;
        var qos = msg?.QoS ?? QualityOfService.AtLeastOnce;
        var logicalName = _naming != null ? _naming(typeof(TMessage)) : TypeNameCache<TMessage>.Name;

        activity?.SetTag(CatgaActivitySource.Tags.MessageType, logicalName);
        activity?.SetTag(CatgaActivitySource.Tags.MessageId, ctx.MessageId);
        // Avoid enum boxing: use static string mapping
        var qosString = qos switch
        {
            QualityOfService.AtMostOnce => "AtMostOnce",
            QualityOfService.AtLeastOnce => "AtLeastOnce",
            QualityOfService.ExactlyOnce => "ExactlyOnce",
            _ => "Unknown"
        };
        activity?.SetTag(CatgaActivitySource.Tags.QoS, qosString);
        activity?.SetTag(CatgaActivitySource.Tags.MessagingSystem, "inmemory");
        activity?.SetTag(CatgaActivitySource.Tags.MessagingDestination, logicalName);
        activity?.SetTag(CatgaActivitySource.Tags.MessagingOperation, "publish");

        CatgaDiagnostics.IncrementActiveMessages();
        try
        {
            switch (qos)
            {
                case QualityOfService.AtMostOnce:
                    // ✅ QoS 0: Best-effort delivery, wait for completion but no retry
                    // Failure is discarded (logged but not thrown)
                    try
                    {
                        await ExecuteHandlersAsync(handlers, message, ctx).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // QoS 0: Discard on failure, log but don't throw
                        if (_logger is not null)
                        {
                            CatgaLog.InMemoryQoS0ProcessingFailed(_logger, ex, ctx.MessageId, logicalName);
                        }
                    }
                    System.Diagnostics.Activity.Current?.AddActivityEvent(CatgaActivitySource.Events.InMemoryPublishSent,
                        ("destination", logicalName),
                        ("qos", qosString));
                    break;

                case QualityOfService.AtLeastOnce:
                    if ((msg?.DeliveryMode ?? DeliveryMode.WaitForResult) == DeliveryMode.WaitForResult)
                    {
                        // Synchronous mode: wait for result
                        await _provider.ExecuteTransportPublishAsync(
                            ct => new ValueTask(ExecuteHandlersAsync(handlers, message, ctx)),
                            cancellationToken);
                    }
                    else
                    {
                        // Asynchronous mode: execute in background with built-in retry via Polly
#if NET8_0_OR_GREATER
                        var retryPipeline = new ResiliencePipelineBuilder()
                            .AddRetry(new RetryStrategyOptions
                            {
                                MaxRetryAttempts = 3,
                                Delay = TimeSpan.FromMilliseconds(100),
                                BackoffType = DelayBackoffType.Exponential,
                                UseJitter = true,
                                ShouldHandle = new PredicateBuilder().Handle<Exception>()
                            })
                            .Build();

                        _ = Task.Run(() => retryPipeline.ExecuteAsync(
                                async ct => { await ExecuteHandlersAsync(handlers, message, ctx); return 0; },
                                cancellationToken),
                            cancellationToken);
#else
                        var retryPolicy = Policy
                            .Handle<Exception>()
                            .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt - 1)));

                        _ = Task.Run(() => retryPolicy.ExecuteAsync(
                                () => ExecuteHandlersAsync(handlers, message, ctx),
                                cancellationToken),
                            cancellationToken);
#endif
                    }
                    break;

                case QualityOfService.ExactlyOnce:
                    if (ctx.MessageId.HasValue && _idempotencyStore.IsProcessed(ctx.MessageId.Value))
                    {
                        activity?.SetTag("catga.idempotent", true);
                        return;
                    }

                    await _provider.ExecuteTransportPublishAsync(
                        ct => new ValueTask(ExecuteHandlersAsync(handlers, message, ctx)),
                        cancellationToken);

                    if (ctx.MessageId.HasValue)
                        _idempotencyStore.MarkAsProcessed(ctx.MessageId.Value);
                    System.Diagnostics.Activity.Current?.AddActivityEvent(CatgaActivitySource.Events.InMemoryPublishSent,
                        ("destination", logicalName),
                        ("qos", qosString));
                    break;
            }

            sw.Stop();
            CatgaDiagnostics.MessagesPublished.Add(1,
                new KeyValuePair<string, object?>("message_type", logicalName),
                new KeyValuePair<string, object?>("qos", qosString),
                new KeyValuePair<string, object?>("component", "Transport.InMemory"),
                new KeyValuePair<string, object?>("destination", logicalName));
            CatgaDiagnostics.MessageDuration.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("message_type", logicalName));
        }
        catch (Exception ex)
        {
            CatgaDiagnostics.MessagesFailed.Add(1,
                new KeyValuePair<string, object?>("message_type", logicalName),
                new KeyValuePair<string, object?>("component", "Transport.InMemory"),
                new KeyValuePair<string, object?>("destination", logicalName));
            RecordException(activity, ex);
            throw;
        }
        finally
        {
            CatgaDiagnostics.DecrementActiveMessages();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static async Task ExecuteHandlersAsync<TMessage>(IReadOnlyList<Func<TMessage, TransportContext, Task>> handlers, TMessage message, TransportContext context) where TMessage : class
    {
        var tasks = new Task[handlers.Count];
        for (int i = 0; i < handlers.Count; i++)
            tasks[i] = InvokeHandlerWithEvent(handlers[i], message, context);

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task InvokeHandlerWithEvent<TMessage>(Func<TMessage, TransportContext, Task> handler, TMessage message, TransportContext context) where TMessage : class
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            await handler(message, context).ConfigureAwait(false);
            var ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            System.Diagnostics.Activity.Current?.AddActivityEvent(CatgaActivitySource.Events.InMemoryReceiveHandler,
                ("duration.ms", ms));
            System.Diagnostics.Activity.Current?.AddActivityEvent(CatgaActivitySource.Events.InMemoryReceiveProcessed);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Activity.Current?.SetError(ex);
            throw;
        }
    }



    public Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message, string destination, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
        => PublishAsync(message, context, cancellationToken);

    public Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(Func<TMessage, TransportContext, Task> handler, CancellationToken cancellationToken = default) where TMessage : class
    {
        TypedSubscribers<TMessage>.AddHandler(handler);
        return Task.CompletedTask;
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
    private static void RecordException(Activity? activity, Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        var typeName = ex.GetType().FullName ?? ex.GetType().Name;
        activity?.AddTag("exception.type", typeName);
        activity?.AddTag("exception.message", ex.Message);
    }
}

/// <summary>Options for configuring InMemory transport</summary>
public class InMemoryTransportOptions
{
    /// <summary>Maximum number of concurrent message processing tasks (default: CPU cores * 2, min 16)</summary>
    public int MaxConcurrency { get; set; } = Math.Max(Environment.ProcessorCount * 2, 16);

    /// <summary>Circuit breaker: consecutive failure threshold before opening (default: 5)</summary>
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>Circuit breaker: duration to keep circuit open (default: 30 seconds)</summary>
    public TimeSpan CircuitBreakerDuration { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Typed subscriber cache (lock-free using ImmutableList + Interlocked.CompareExchange)
/// Inspired by Snowflake ID generator's CAS pattern for zero-lock concurrency
/// </summary>
internal static class TypedSubscribers<TMessage> where TMessage : class
{
    private static ImmutableList<Func<TMessage, TransportContext, Task>> _handlers = ImmutableList<Func<TMessage, TransportContext, Task>>.Empty;

    /// <summary>
    /// Get current handlers snapshot (lock-free read via Volatile)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ImmutableList<Func<TMessage, TransportContext, Task>> GetHandlers() =>
        Volatile.Read(ref _handlers);

    /// <summary>
    /// Add handler (lock-free using CAS loop like SnowflakeIdGenerator)
    /// </summary>
    public static void AddHandler(Func<TMessage, TransportContext, Task> handler)
    {
        while (true)
        {
            var current = Volatile.Read(ref _handlers);
            var next = current.Add(handler);

            // CAS: if _handlers is still 'current', replace with 'next'
            if (Interlocked.CompareExchange(ref _handlers, next, current) == current)
                return;

            // Retry if another thread modified _handlers
        }
    }
}

