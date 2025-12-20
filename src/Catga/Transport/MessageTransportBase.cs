using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Abstractions;
using Catga.Core;
using Catga.Observability;
using Catga.Resilience;

namespace Catga.Transport;

/// <summary>Base class for message transports with common functionality.</summary>
public abstract class MessageTransportBase : IMessageTransport
{
    protected readonly IMessageSerializer Serializer;
    protected readonly IResiliencePipelineProvider ResilienceProvider;
    protected readonly string Prefix;
    protected readonly Func<Type, string>? Naming;
    protected readonly CancellationTokenSource Cts = new();

    // Batch queue
    private readonly ConcurrentQueue<BatchItem> _batchQueue = new();
    private int _batchCount;
    private int _flushing; // 0 = not flushing, 1 = flushing
    private Timer? _batchTimer;

    public abstract string Name { get; }
    public virtual BatchTransportOptions? BatchOptions => null;
    public virtual CompressionTransportOptions? CompressionOptions => null;

    protected MessageTransportBase(
        IMessageSerializer serializer,
        IResiliencePipelineProvider provider,
        string? prefix = null,
        Func<Type, string>? naming = null)
    {
        Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        ResilienceProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        var p = prefix ?? "catga.";
        Prefix = p.EndsWith('.') ? p : p + ".";
        Naming = naming;
    }

    /// <summary>Initialize batch timer if auto-batching is enabled.</summary>
    protected void InitializeBatchTimer(BatchTransportOptions? batchOptions)
    {
        if (batchOptions is { EnableAutoBatching: true })
        {
            _batchTimer = new Timer(
                static s => ((MessageTransportBase)s!).TryFlushBatchTimer(),
                this,
                batchOptions.BatchTimeout,
                batchOptions.BatchTimeout);
        }
    }

    #region Subject/Destination Naming

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected string GetSubject<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>() where TMessage : class
    {
        var name = Naming != null ? Naming(typeof(TMessage)) : TypeNameCache<TMessage>.Name;
        return Prefix + name;
    }

    #endregion

    #region Activity Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static Activity? StartPublishActivity(string system, string destination, string messageType, string? messageId = null)
    {
        if (!ObservabilityHooks.IsEnabled) return null;
        var activity = CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Publish", ActivityKind.Producer);
        SetActivityTags(activity, system, destination, messageType, messageId);
        return activity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static Activity? StartReceiveActivity(string system, string destination, string messageType, string? traceParent = null, string? traceState = null)
    {
        if (!ObservabilityHooks.IsEnabled) return null;

        Activity? activity;
        if (!string.IsNullOrEmpty(traceParent))
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

        SetActivityTags(activity, system, destination, messageType);
        return activity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static Activity? StartBatchFlushActivity()
    {
        if (!ObservabilityHooks.IsEnabled) return null;
        return CatgaDiagnostics.ActivitySource.StartActivity("Messaging.Batch.Flush", ActivityKind.Producer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetActivityTags(Activity? activity, string system, string destination, string messageType, string? messageId = null)
    {
        if (activity == null) return;
        activity.SetTag(CatgaActivitySource.Tags.MessagingSystem, system);
        activity.SetTag(CatgaActivitySource.Tags.MessagingDestination, destination);
        activity.SetTag(CatgaActivitySource.Tags.MessageType, messageType);
        if (messageId != null)
            activity.SetTag(CatgaActivitySource.Tags.MessageId, messageId);
    }

    #endregion

    #region Metrics Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void RecordPublishSuccess(string messageType, string destination)
    {
        if (!ObservabilityHooks.IsEnabled) return;
        CatgaDiagnostics.MessagesPublished.Add(1,
            new KeyValuePair<string, object?>("component", $"Transport.{Name}"),
            new KeyValuePair<string, object?>("message_type", messageType),
            new KeyValuePair<string, object?>("destination", destination));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void RecordPublishFailure(string destination, string? reason = null)
    {
        if (!ObservabilityHooks.IsEnabled) return;
        CatgaDiagnostics.MessagesFailed.Add(1,
            new KeyValuePair<string, object?>("component", $"Transport.{Name}"),
            new KeyValuePair<string, object?>("destination", destination),
            new KeyValuePair<string, object?>("reason", reason ?? "publish"));
    }

    #endregion

    #region Batch Queue Management

    protected readonly record struct BatchItem(
        string Destination,
        byte[] Payload,
        string? TraceParent,
        string? TraceState,
        object? Extra);

    protected void EnqueueBatch(BatchItem item, BatchTransportOptions batchOptions, int maxQueueLength)
    {
        var newCount = Interlocked.Increment(ref _batchCount);

        // Backpressure: drop oldest when exceeding MaxQueueLength
        if (maxQueueLength > 0 && newCount > maxQueueLength)
        {
            while (Interlocked.CompareExchange(ref _batchCount, _batchCount, _batchCount) > maxQueueLength
                   && _batchQueue.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _batchCount);
            }
        }

        _batchQueue.Enqueue(item);

        // Immediate flush by size
        if (newCount >= batchOptions.MaxBatchSize)
            TryFlushBatchImmediate(batchOptions);
    }

    private void TryFlushBatchImmediate(BatchTransportOptions batch)
    {
        // Lock-free: only one thread can flush at a time
        if (Interlocked.CompareExchange(ref _flushing, 1, 0) != 0) return;
        try
        {
            FlushBatchInternalAsync(batch).GetAwaiter().GetResult();
        }
        finally
        {
            Interlocked.Exchange(ref _flushing, 0);
        }
    }

    private void TryFlushBatchTimer()
    {
        var batch = BatchOptions;
        if (batch is not { EnableAutoBatching: true }) return;
        if (Interlocked.CompareExchange(ref _flushing, 1, 0) != 0) return;
        try
        {
            FlushBatchInternalAsync(batch).GetAwaiter().GetResult();
        }
        finally
        {
            Interlocked.Exchange(ref _flushing, 0);
        }
    }

    private async Task FlushBatchInternalAsync(BatchTransportOptions batch)
    {
        var toProcess = Math.Min(_batchCount, batch.MaxBatchSize);
        if (toProcess <= 0) return;

        var list = new List<BatchItem>(toProcess);
        while (toProcess-- > 0 && _batchQueue.TryDequeue(out var item))
        {
            Interlocked.Decrement(ref _batchCount);
            list.Add(item);
        }

        using var batchSpan = StartBatchFlushActivity();
        await ProcessBatchItemsAsync(list, batchSpan).ConfigureAwait(false);
    }

    /// <summary>Override to process batch items specific to the transport.</summary>
    protected virtual Task ProcessBatchItemsAsync(List<BatchItem> items, Activity? batchSpan)
        => Task.CompletedTask;

    #endregion

    #region Abstract Methods

    public abstract Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        TransportContext? context = null,
        CancellationToken cancellationToken = default) where TMessage : class;

    public abstract Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        string destination,
        TransportContext? context = null,
        CancellationToken cancellationToken = default) where TMessage : class;

    public abstract Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken cancellationToken = default) where TMessage : class;

    #endregion

    #region Batch Operations (Default Implementation)

    public virtual async Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        IEnumerable<TMessage> messages,
        TransportContext? context = null,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        await BatchOperationHelper.ExecuteBatchAsync(
            messages,
            m => PublishAsync(m, context, cancellationToken));
    }

    public virtual async Task SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        IEnumerable<TMessage> messages,
        string destination,
        TransportContext? context = null,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        await BatchOperationHelper.ExecuteBatchAsync(
            messages,
            destination,
            (m, dest) => SendAsync(m, dest, context, cancellationToken));
    }

    #endregion

    #region Dispose

    protected virtual async ValueTask DisposeAsyncCore()
    {
        await Cts.CancelAsync();
        _batchTimer?.Dispose();
        Cts.Dispose();
    }

    #endregion
}
