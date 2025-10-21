using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using Catga.Abstractions;
using Catga.Core;
using Catga.Idempotency;
using Catga.Observability;
using Catga.Resilience;
using Microsoft.Extensions.Logging;

namespace Catga.Transport;

/// <summary>In-memory message transport (for testing, supports QoS)</summary>
public class InMemoryMessageTransport : IMessageTransport, IDisposable
{
    private readonly InMemoryIdempotencyStore _idempotencyStore = new();
    private readonly ConcurrencyLimiter _concurrencyLimiter;
    private readonly CircuitBreaker _circuitBreaker;
    private readonly ILogger<InMemoryMessageTransport>? _logger;

    public InMemoryMessageTransport(
        InMemoryTransportOptions? options = null,
        ILogger<InMemoryMessageTransport>? logger = null)
    {
        _logger = logger;
        options ??= new InMemoryTransportOptions();

        _concurrencyLimiter = new ConcurrencyLimiter(
            options.MaxConcurrency,
            logger);

        _circuitBreaker = new CircuitBreaker(
            options.CircuitBreakerThreshold,
            options.CircuitBreakerDuration,
            logger);
    }

    public string Name => "InMemory";
    public BatchTransportOptions? BatchOptions => null;
    public CompressionTransportOptions? CompressionOptions => null;

    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
    {
        using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Message.Publish", ActivityKind.Producer);
        var sw = Stopwatch.StartNew();

        // Get immutable snapshot (lock-free read via volatile)
        var handlers = TypedSubscribers<TMessage>.GetHandlers();
        if (handlers.Count == 0) return;

        var ctx = context ?? new TransportContext { MessageId = MessageExtensions.NewMessageId(), MessageType = TypeNameCache<TMessage>.FullName, SentAt = DateTime.UtcNow };
        var msg = message as IMessage;
        var qos = msg?.QoS ?? QualityOfService.AtLeastOnce;

        activity?.SetTag("catga.message.type", TypeNameCache<TMessage>.Name);
        activity?.SetTag("catga.message.id", ctx.MessageId);
        // ✅ Avoid enum boxing: use static string mapping
        var qosString = qos switch
        {
            QualityOfService.AtMostOnce => "AtMostOnce",
            QualityOfService.AtLeastOnce => "AtLeastOnce",
            QualityOfService.ExactlyOnce => "ExactlyOnce",
            _ => "Unknown"
        };
        activity?.SetTag("catga.qos", qosString);

        CatgaDiagnostics.IncrementActiveMessages();
        try
        {
            switch (qos)
            {
                case QualityOfService.AtMostOnce:
                    // ✅ QoS 0: Best-effort delivery, wait for completion but no retry
                    // Failure is discarded (logged but not thrown)
                    using (await _concurrencyLimiter.AcquireAsync(cancellationToken).ConfigureAwait(false))
                    {
                        try
                        {
                            await _circuitBreaker.ExecuteAsync(() =>
                                ExecuteHandlersAsync(handlers, message, ctx)).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            // QoS 0: Discard on failure, log but don't throw
                            _logger?.LogWarning(ex,
                                "QoS 0 message processing failed, discarding. MessageId: {MessageId}, Type: {MessageType}",
                                ctx.MessageId, TypeNameCache<TMessage>.Name);
                        }
                    }
                    break;

                case QualityOfService.AtLeastOnce:
                    if ((msg?.DeliveryMode ?? DeliveryMode.WaitForResult) == DeliveryMode.WaitForResult)
                    {
                        // Synchronous mode: wait for result
                        using (await _concurrencyLimiter.AcquireAsync(cancellationToken).ConfigureAwait(false))
                        {
                            await _circuitBreaker.ExecuteAsync(() =>
                                ExecuteHandlersAsync(handlers, message, ctx)).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        // Asynchronous mode: background retry
                        DeliverWithRetryAsync(handlers, message, ctx, cancellationToken);
                    }
                    break;

                case QualityOfService.ExactlyOnce:
                    if (ctx.MessageId.HasValue && _idempotencyStore.IsProcessed(ctx.MessageId.Value))
                    {
                        activity?.SetTag("catga.idempotent", true);
                        return;
                    }

                    using (await _concurrencyLimiter.AcquireAsync(cancellationToken).ConfigureAwait(false))
                    {
                        await _circuitBreaker.ExecuteAsync(() =>
                            ExecuteHandlersAsync(handlers, message, ctx)).ConfigureAwait(false);
                    }

                    if (ctx.MessageId.HasValue)
                        _idempotencyStore.MarkAsProcessed(ctx.MessageId.Value);
                    break;
            }

            sw.Stop();
            // ✅ Use TagList to avoid heap allocation
            var publishedTags = new TagList
            {
                { "message_type", TypeNameCache<TMessage>.Name },
                { "qos", qosString }
            };
            var durationTags = new TagList { { "message_type", TypeNameCache<TMessage>.Name } };
            CatgaDiagnostics.MessagesPublished.Add(1, publishedTags);
            CatgaDiagnostics.MessageDuration.Record(sw.Elapsed.TotalMilliseconds, durationTags);
        }
        catch (Exception ex)
        {
            // ✅ Use TagList to avoid heap allocation
            var failedTags = new TagList { { "message_type", TypeNameCache<TMessage>.Name } };
            CatgaDiagnostics.MessagesFailed.Add(1, failedTags);
            RecordException(activity, ex);
            throw;
        }
        finally
        {
            CatgaDiagnostics.DecrementActiveMessages();
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static async Task ExecuteHandlersAsync<TMessage>(IReadOnlyList<Delegate> handlers, TMessage message, TransportContext context) where TMessage : class
    {
        var tasks = new Task[handlers.Count];
        for (int i = 0; i < handlers.Count; i++)
            tasks[i] = ((Func<TMessage, TransportContext, Task>)handlers[i])(message, context);

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private void DeliverWithRetryAsync<TMessage>(IReadOnlyList<Delegate> handlers, TMessage message, TransportContext context, CancellationToken cancellationToken) where TMessage : class
    {
        // Use Task.Run to ensure execution on thread pool
        _ = Task.Run(async () =>
        {
            using (await _concurrencyLimiter.AcquireAsync(cancellationToken).ConfigureAwait(false))
            {
                for (int attempt = 0; attempt <= 3; attempt++)
                {
                    try
                    {
                        await _circuitBreaker.ExecuteAsync(() =>
                            ExecuteHandlersAsync(handlers, message, context)).ConfigureAwait(false);
                        return;
                    }
                    catch (CircuitBreakerOpenException)
                    {
                        // Circuit breaker is open, stop retrying
                        _logger?.LogWarning(
                            "Circuit breaker open, stopping retry for message {MessageId}",
                            context.MessageId);
                        break;
                    }
                    catch when (attempt < 3)
                    {
                        // Exponential backoff
                        await Task.Delay(
                            TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt)),
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // Final retry failed, log and discard
                        _logger?.LogError(ex,
                            "Final retry failed for message {MessageId} after {Attempts} attempts",
                            context.MessageId, attempt + 1);
                    }
                }
            }
        }, cancellationToken);
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

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void RecordException(Activity? activity, Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.AddTag("exception.type", ExceptionTypeCache.GetFullTypeName(ex));
        activity?.AddTag("exception.message", ex.Message);
    }

    public void Dispose()
    {
        _concurrencyLimiter?.Dispose();
        _circuitBreaker?.Dispose();
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
    private static ImmutableList<Delegate> _handlers = ImmutableList<Delegate>.Empty;

    /// <summary>
    /// Get current handlers snapshot (lock-free read via Volatile)
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static ImmutableList<Delegate> GetHandlers() =>
        Volatile.Read(ref _handlers);

    /// <summary>
    /// Add handler (lock-free using CAS loop like SnowflakeIdGenerator)
    /// </summary>
    public static void AddHandler(Delegate handler)
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

