using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Abstractions;
using Catga.Configuration;
using Catga.Core;
using Catga.Idempotency;
using Catga.Observability;
using Catga.Resilience;
using Microsoft.Extensions.Logging;
using Polly;
#if NET8_0_OR_GREATER
using Polly.Retry;
#endif

namespace Catga.Transport;

/// <summary>In-memory message transport for testing with QoS support.</summary>
public class InMemoryMessageTransport(ILogger<InMemoryMessageTransport>? logger, IResiliencePipelineProvider provider, CatgaOptions? globalOptions = null)
    : IMessageTransport
{
    private readonly InMemoryIdempotencyStore _idem = new();
    private readonly Func<Type, string>? _naming = globalOptions?.EndpointNamingConvention;

    public string Name => "InMemory";
    public BatchTransportOptions? BatchOptions => null;
    public CompressionTransportOptions? CompressionOptions => null;

    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
    {
        using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Publish", ActivityKind.Producer);
        var sw = Stopwatch.StartNew();

        var handlers = TypedSubscribers<TMessage>.GetHandlers();
        if (handlers.Count == 0) return;

        var ctx = context ?? new TransportContext { MessageId = MessageExtensions.NewMessageId(), MessageType = TypeNameCache<TMessage>.FullName, SentAt = DateTime.UtcNow };
        var msg = message as IMessage;
        var qos = msg?.QoS ?? QualityOfService.AtLeastOnce;
        var logicalName = _naming != null ? _naming(typeof(TMessage)) : TypeNameCache<TMessage>.Name;

        activity?.SetTag(CatgaActivitySource.Tags.MessageType, logicalName);
        activity?.SetTag(CatgaActivitySource.Tags.MessageId, ctx.MessageId);
        activity?.SetTag(CatgaActivitySource.Tags.MessagingSystem, "inmemory");
        activity?.SetTag(CatgaActivitySource.Tags.MessagingDestination, logicalName);

        CatgaDiagnostics.IncrementActiveMessages();
        try
        {
            switch (qos)
            {
                case QualityOfService.AtMostOnce:
                    try
                    {
                        await ExecuteHandlersAsync(handlers, message, ctx).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "QoS0 processing failed for {MessageId} {MessageType}", ctx.MessageId, logicalName);
                    }
                    break;

                case QualityOfService.AtLeastOnce:
                    if ((msg?.DeliveryMode ?? DeliveryMode.WaitForResult) == DeliveryMode.WaitForResult)
                    {
                        await provider.ExecuteTransportPublishAsync(ct => new ValueTask(ExecuteHandlersAsync(handlers, message, ctx)), cancellationToken);
                    }
                    else
                    {
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
                        _ = Task.Run(() => retryPipeline.ExecuteAsync(async ct => { await ExecuteHandlersAsync(handlers, message, ctx); return 0; }, cancellationToken), cancellationToken);
#else
                        var retryPolicy = Policy.Handle<Exception>().WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt - 1)));
                        _ = Task.Run(() => retryPolicy.ExecuteAsync(() => ExecuteHandlersAsync(handlers, message, ctx), cancellationToken), cancellationToken);
#endif
                    }
                    break;

                case QualityOfService.ExactlyOnce:
                    if (ctx.MessageId.HasValue && _idem.IsProcessed(ctx.MessageId.Value))
                    {
                        activity?.SetTag("catga.idempotent", true);
                        return;
                    }
                    await provider.ExecuteTransportPublishAsync(ct => new ValueTask(ExecuteHandlersAsync(handlers, message, ctx)), cancellationToken);
                    if (ctx.MessageId.HasValue) _idem.MarkAsProcessed(ctx.MessageId.Value);
                    break;
            }

            sw.Stop();
            CatgaDiagnostics.MessagesPublished.Add(1, new KeyValuePair<string, object?>("message_type", logicalName));
        }
        catch (Exception ex)
        {
            CatgaDiagnostics.MessagesFailed.Add(1, new KeyValuePair<string, object?>("message_type", logicalName));
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
            tasks[i] = handlers[i](message, context);
        await Task.WhenAll(tasks).ConfigureAwait(false);
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
        await BatchOperationHelper.ExecuteBatchAsync(messages, m => PublishAsync(m, context, cancellationToken));
    }

    public Task SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(IEnumerable<TMessage> messages, string destination, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
        => PublishBatchAsync(messages, context, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void RecordException(Activity? activity, Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.AddTag("exception.type", ex.GetType().FullName ?? ex.GetType().Name);
        activity?.AddTag("exception.message", ex.Message);
    }
}

/// <summary>Typed subscriber cache (lock-free using ImmutableList + Interlocked.CompareExchange)</summary>
internal static class TypedSubscribers<TMessage> where TMessage : class
{
    private static ImmutableList<Func<TMessage, TransportContext, Task>> _handlers = ImmutableList<Func<TMessage, TransportContext, Task>>.Empty;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ImmutableList<Func<TMessage, TransportContext, Task>> GetHandlers() => Volatile.Read(ref _handlers);

    public static void AddHandler(Func<TMessage, TransportContext, Task> handler)
    {
        while (true)
        {
            var current = Volatile.Read(ref _handlers);
            var next = current.Add(handler);
            if (Interlocked.CompareExchange(ref _handlers, next, current) == current) return;
        }
    }
}
