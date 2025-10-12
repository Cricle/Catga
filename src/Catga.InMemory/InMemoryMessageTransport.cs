using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Catga.Common;
using Catga.Core;
using Catga.Idempotency;
using Catga.Messages;
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

        var handlers = TypedSubscribers<TMessage>.Handlers;
        if (handlers.Count == 0) return;

        var ctx = context ?? new TransportContext { MessageId = Guid.NewGuid().ToString(), MessageType = TypeNameCache<TMessage>.FullName, SentAt = DateTime.UtcNow };
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
                    {
                        using var rented = ArrayPoolHelper.RentOrAllocate<Task>(handlers.Count);
                        for (int i = 0; i < handlers.Count; i++)
                            rented.Array[i] = ((Func<TMessage, TransportContext, Task>)handlers[i])(message, ctx);
                        await Task.WhenAll(rented.AsSpan().ToArray());
                    }
                    else
                        _ = DeliverWithRetryAsync(handlers, message, ctx, cancellationToken);
                    break;

                case QualityOfService.ExactlyOnce:
                    if (ctx.MessageId != null && _idempotencyStore.IsProcessed(ctx.MessageId))
                    {
                        activity?.SetTag("catga.idempotent", true);
                        return;
                    }

                    using (var rented = ArrayPoolHelper.RentOrAllocate<Task>(handlers.Count))
                    {
                        for (int i = 0; i < handlers.Count; i++)
                            rented.Array[i] = ((Func<TMessage, TransportContext, Task>)handlers[i])(message, ctx);
                        await Task.WhenAll(rented.AsSpan().ToArray());
                    }

                    if (ctx.MessageId != null)
                        _idempotencyStore.MarkAsProcessed(ctx.MessageId);
                    break;
            }

            sw.Stop();
            CatgaDiagnostics.MessagesPublished.Add(1, new KeyValuePair<string, object?>("message_type", TypeNameCache<TMessage>.Name), new KeyValuePair<string, object?>("qos", qos.ToString()));
            CatgaDiagnostics.MessageDuration.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("message_type", TypeNameCache<TMessage>.Name));
        }
        catch (Exception ex)
        {
            CatgaDiagnostics.MessagesFailed.Add(1, new KeyValuePair<string, object?>("message_type", TypeNameCache<TMessage>.Name));
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddTag("exception.type", ex.GetType().FullName);
            activity?.AddTag("exception.message", ex.Message);
            throw;
        }
        finally
        {
            CatgaDiagnostics.DecrementActiveMessages();
        }
    }

    private static async ValueTask FireAndForgetAsync<TMessage>(List<Delegate> handlers, TMessage message, TransportContext context, CancellationToken cancellationToken) where TMessage : class
    {
        try
        {
            using var rented = ArrayPoolHelper.RentOrAllocate<Task>(handlers.Count);
            for (int i = 0; i < handlers.Count; i++)
                rented.Array[i] = ((Func<TMessage, TransportContext, Task>)handlers[i])(message, context);
            await Task.WhenAll(rented.AsSpan().ToArray()).ConfigureAwait(false);
        }
        catch { }
    }

    private static async ValueTask DeliverWithRetryAsync<TMessage>(List<Delegate> handlers, TMessage message, TransportContext context, CancellationToken cancellationToken) where TMessage : class
    {
        for (int attempt = 0; attempt <= 3; attempt++)
        {
            try
            {
                using var rented = ArrayPoolHelper.RentOrAllocate<Task>(handlers.Count);
                for (int i = 0; i < handlers.Count; i++)
                    rented.Array[i] = ((Func<TMessage, TransportContext, Task>)handlers[i])(message, context);
                await Task.WhenAll(rented.AsSpan().ToArray()).ConfigureAwait(false);
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
        lock (TypedSubscribers<TMessage>.Lock)
        {
            TypedSubscribers<TMessage>.Handlers.Add(handler);
        }
        return Task.CompletedTask;
    }

    public async Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(IEnumerable<TMessage> messages, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
    {
        foreach (var message in messages)
            await PublishAsync(message, context, cancellationToken);
    }

    public Task SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(IEnumerable<TMessage> messages, string destination, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
        => PublishBatchAsync(messages, context, cancellationToken);
}

