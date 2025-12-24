using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Abstractions;
using Catga.Configuration;
using Catga.Core;
using Catga.Hosting;
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
public sealed class InMemoryMessageTransport(
    ILogger<InMemoryMessageTransport>? logger,
    IResiliencePipelineProvider provider,
    CatgaOptions? globalOptions = null)
    : MessageTransportBase(new NullSerializer(), provider, "inmemory.", globalOptions?.EndpointNamingConvention), IAsyncInitializable, IStoppable, IWaitable, IHealthCheckable
{
    private readonly InMemoryIdempotencyStore _idem = new();

    // IStoppable implementation
    private volatile bool _acceptingMessages = true;
    
    // IWaitable implementation
    private int _pendingOperations = 0;
    
    // IHealthCheckable implementation
    private volatile bool _isHealthy = true; // In-memory is always healthy
    private DateTimeOffset? _lastHealthCheck;

    public override string Name => "InMemory";
    
    // IStoppable properties
    public bool IsAcceptingMessages => _acceptingMessages;
    
    // IWaitable properties
    public int PendingOperations => _pendingOperations;
    
    // IHealthCheckable properties
    public bool IsHealthy => _isHealthy;
    public string? HealthStatus => "In-Memory (Always Available)";
    public DateTimeOffset? LastHealthCheck => _lastHealthCheck;

    // IAsyncInitializable implementation
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _isHealthy = true;
        _lastHealthCheck = DateTimeOffset.UtcNow;
        logger?.LogInformation("InMemory transport initialized");
        return Task.CompletedTask;
    }

    // IStoppable implementation
    public void StopAcceptingMessages()
    {
        _acceptingMessages = false;
        logger?.LogInformation("InMemory transport stopped accepting new messages");
    }

    // IWaitable implementation
    public async Task WaitForCompletionAsync(CancellationToken cancellationToken = default)
    {
        logger?.LogInformation("Waiting for {Count} pending operations to complete", _pendingOperations);
        
        var timeout = TimeSpan.FromSeconds(30);
        var sw = Stopwatch.StartNew();
        
        while (_pendingOperations > 0 && sw.Elapsed < timeout)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                logger?.LogWarning("Wait for completion cancelled with {Count} operations pending", _pendingOperations);
                break;
            }
            
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
        
        if (_pendingOperations > 0)
        {
            logger?.LogWarning("Timeout waiting for operations to complete. {Count} operations still pending", _pendingOperations);
        }
        else
        {
            logger?.LogInformation("All pending operations completed");
        }
    }

    public override async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
    {
        // Check if accepting messages
        if (!_acceptingMessages)
        {
            throw new InvalidOperationException("Transport is not accepting new messages");
        }
        
        Interlocked.Increment(ref _pendingOperations);
        try
        {
            var handlers = TypedSubscribers<TMessage>.GetHandlers();
            if (handlers.Count == 0) return;

            var ctx = context ?? new TransportContext
            {
                MessageId = MessageExtensions.NewMessageId(),
                MessageType = TypeNameCache<TMessage>.FullName,
                SentAt = DateTime.UtcNow
            };

            var msg = message as IMessage;
            var qos = msg?.QoS ?? QualityOfService.AtLeastOnce;
            var logicalName = Naming != null ? Naming(typeof(TMessage)) : TypeNameCache<TMessage>.Name;

            using var activity = StartPublishActivity("inmemory", logicalName, logicalName, ctx.MessageId?.ToString());

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
                            await ResilienceProvider.ExecuteTransportPublishAsync(
                                _ => new ValueTask(ExecuteHandlersAsync(handlers, message, ctx)),
                                cancellationToken);
                        }
                        else
                        {
                            _ = ExecuteFireAndForgetAsync(handlers, message, ctx, cancellationToken);
                        }
                        break;

                    case QualityOfService.ExactlyOnce:
                        if (ctx.MessageId.HasValue && _idem.IsProcessed(ctx.MessageId.Value))
                        {
                            activity?.SetTag("catga.idempotent", true);
                            return;
                        }
                        await ResilienceProvider.ExecuteTransportPublishAsync(
                            _ => new ValueTask(ExecuteHandlersAsync(handlers, message, ctx)),
                            cancellationToken);
                        if (ctx.MessageId.HasValue) _idem.MarkAsProcessed(ctx.MessageId.Value);
                        break;
                }

                RecordPublishSuccess(logicalName, logicalName);
            }
            catch (Exception ex)
            {
                RecordPublishFailure(logicalName);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw;
            }
            finally
            {
                CatgaDiagnostics.DecrementActiveMessages();
            }
        }
        finally
        {
            Interlocked.Decrement(ref _pendingOperations);
        }
    }

    public override Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        string destination,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        => PublishAsync(message, context, cancellationToken);

    public override Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken cancellationToken = default)
    {
        TypedSubscribers<TMessage>.AddHandler(handler);
        return Task.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static async Task ExecuteHandlersAsync<TMessage>(
        IReadOnlyList<Func<TMessage, TransportContext, Task>> handlers,
        TMessage message,
        TransportContext context) where TMessage : class
    {
        var tasks = new Task[handlers.Count];
        for (var i = 0; i < handlers.Count; i++)
            tasks[i] = handlers[i](message, context);
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static Task ExecuteFireAndForgetAsync<TMessage>(
        IReadOnlyList<Func<TMessage, TransportContext, Task>> handlers,
        TMessage message,
        TransportContext ctx,
        CancellationToken cancellationToken) where TMessage : class
    {
#if NET8_0_OR_GREATER
        var retryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(50),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = false,
                ShouldHandle = new PredicateBuilder().Handle<Exception>()
            })
            .Build();
        return Task.Run(() => retryPipeline.ExecuteAsync(async _ =>
        {
            await ExecuteHandlersAsync(handlers, message, ctx);
            return 0;
        }, cancellationToken), cancellationToken);
#else
        var retryPolicy = Policy.Handle<Exception>()
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(50 * Math.Pow(2, attempt - 1)));
        return Task.Run(() => retryPolicy.ExecuteAsync(() => ExecuteHandlersAsync(handlers, message, ctx), cancellationToken), cancellationToken);
#endif
    }

    /// <summary>Null serializer for in-memory transport (no serialization needed).</summary>
    private sealed class NullSerializer : IMessageSerializer
    {
        public string Name => "Null";
        public byte[] Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value) => [];
        public T Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(byte[] data) => default!;
        public T Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(ReadOnlySpan<byte> data) => default!;
        public void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value, System.Buffers.IBufferWriter<byte> bufferWriter) { }
        public byte[] Serialize(object value, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type) => [];
        public object? Deserialize(byte[] data, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type) => null;
        public object? Deserialize(ReadOnlySpan<byte> data, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type) => null;
        public void Serialize(object value, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type, System.Buffers.IBufferWriter<byte> bufferWriter) { }
    }
}

/// <summary>Typed subscriber cache (lock-free using ImmutableList + Interlocked.CompareExchange)</summary>
internal static class TypedSubscribers<TMessage> where TMessage : class
{
    private static ImmutableList<Func<TMessage, TransportContext, Task>> _handlers =
        ImmutableList<Func<TMessage, TransportContext, Task>>.Empty;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ImmutableList<Func<TMessage, TransportContext, Task>> GetHandlers()
        => Volatile.Read(ref _handlers);

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
