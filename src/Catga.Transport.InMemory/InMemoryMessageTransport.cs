using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Catga.Idempotency;
using Catga.Observability;

namespace Catga.Transport;

/// <summary>In-memory message transport (for testing, supports QoS)</summary>
public class InMemoryMessageTransport : IMessageTransport
{
    private readonly InMemoryIdempotencyStore _idempotencyStore = new();

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
        activity?.SetTag("catga.qos", qos.ToString());

        CatgaDiagnostics.IncrementActiveMessages();
        try
        {
            switch (qos)
            {
                case QualityOfService.AtMostOnce:
                    _ = FireAndForgetAsync(handlers, message, ctx, cancellationToken);
                    break;

                case QualityOfService.AtLeastOnce:
                    if ((msg?.DeliveryMode ?? DeliveryMode.WaitForResult) == DeliveryMode.WaitForResult)
                        await ExecuteHandlersAsync(handlers, message, ctx);
                    else
                        _ = DeliverWithRetryAsync(handlers, message, ctx, cancellationToken);
                    break;

                case QualityOfService.ExactlyOnce:
                    if (ctx.MessageId.HasValue && _idempotencyStore.IsProcessed(ctx.MessageId.Value))
                    {
                        activity?.SetTag("catga.idempotent", true);
                        return;
                    }

                    await ExecuteHandlersAsync(handlers, message, ctx);

                    if (ctx.MessageId.HasValue)
                        _idempotencyStore.MarkAsProcessed(ctx.MessageId.Value);
                    break;
            }

            sw.Stop();
            CatgaDiagnostics.MessagesPublished.Add(1, new KeyValuePair<string, object?>("message_type", TypeNameCache<TMessage>.Name), new KeyValuePair<string, object?>("qos", qos.ToString()));
            CatgaDiagnostics.MessageDuration.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("message_type", TypeNameCache<TMessage>.Name));
        }
        catch (Exception ex)
        {
            CatgaDiagnostics.MessagesFailed.Add(1, new KeyValuePair<string, object?>("message_type", TypeNameCache<TMessage>.Name));
            RecordException(activity, ex);
            throw;
        }
        finally
        {
            CatgaDiagnostics.DecrementActiveMessages();
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static async ValueTask ExecuteHandlersAsync<TMessage>(IReadOnlyList<Delegate> handlers, TMessage message, TransportContext context) where TMessage : class
    {
        var tasks = new Task[handlers.Count];
        for (int i = 0; i < handlers.Count; i++)
            tasks[i] = ((Func<TMessage, TransportContext, Task>)handlers[i])(message, context);

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async ValueTask FireAndForgetAsync<TMessage>(IReadOnlyList<Delegate> handlers, TMessage message, TransportContext context, CancellationToken cancellationToken) where TMessage : class
    {
        try { await ExecuteHandlersAsync(handlers, message, context); }
        catch { }
    }

    private static async ValueTask DeliverWithRetryAsync<TMessage>(IReadOnlyList<Delegate> handlers, TMessage message, TransportContext context, CancellationToken cancellationToken) where TMessage : class
    {
        for (int attempt = 0; attempt <= 3; attempt++)
        {
            try
            {
                await ExecuteHandlersAsync(handlers, message, context);
                return;
            }
            catch when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt)), cancellationToken).ConfigureAwait(false);
            }
            catch { }
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

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void RecordException(Activity? activity, Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.AddTag("exception.type", ExceptionTypeCache.GetFullTypeName(ex));
        activity?.AddTag("exception.message", ex.Message);
    }
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

