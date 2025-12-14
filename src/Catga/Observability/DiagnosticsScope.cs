using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;

namespace Catga.Observability;

/// <summary>
/// Scoped diagnostics helper that automatically tracks duration and handles errors.
/// Simplifies repetitive diagnostics patterns across stores and transports.
/// </summary>
public readonly struct DiagnosticsScope : IDisposable
{
    private readonly long _startTimestamp;
    private readonly Activity? _activity;
    private readonly Counter<long>? _successCounter;
    private readonly Counter<long>? _failureCounter;
    private readonly Histogram<double>? _durationHistogram;
    private readonly KeyValuePair<string, object?>[]? _tags;

    /// <summary>
    /// Create a new diagnostics scope with activity and metrics tracking.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DiagnosticsScope(
        string activityName,
        ActivityKind kind = ActivityKind.Internal,
        Counter<long>? successCounter = null,
        Counter<long>? failureCounter = null,
        Histogram<double>? durationHistogram = null,
        params KeyValuePair<string, object?>[] tags)
    {
        _startTimestamp = Stopwatch.GetTimestamp();
        _activity = CatgaDiagnostics.ActivitySource.StartActivity(activityName, kind);
        _successCounter = successCounter;
        _failureCounter = failureCounter;
        _durationHistogram = durationHistogram;
        _tags = tags.Length > 0 ? tags : null;

        if (_activity != null && _tags != null)
        {
            foreach (var tag in _tags)
                _activity.SetTag(tag.Key, tag.Value);
        }
    }

    /// <summary>Current activity for adding custom events/tags.</summary>
    public Activity? Activity => _activity;

    /// <summary>Elapsed milliseconds since scope creation.</summary>
    public double ElapsedMs
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;
    }

    /// <summary>Add an event to the current activity.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddEvent(string name, params KeyValuePair<string, object?>[] tags)
    {
        if (_activity == null) return;
        _activity.AddEvent(new ActivityEvent(name, tags: new ActivityTagsCollection(tags)));
    }

    /// <summary>Mark the scope as failed with an exception.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetError(Exception ex)
    {
        _activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        _activity?.SetTag("error.type", ex.GetType().Name);
        _activity?.SetTag("error.message", ex.Message);
    }

    /// <summary>Record success metrics.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordSuccess(long count = 1)
    {
        if (_tags != null)
            _successCounter?.Add(count, _tags);
        else
            _successCounter?.Add(count);
    }

    /// <summary>Record failure metrics.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordFailure(long count = 1)
    {
        if (_tags != null)
            _failureCounter?.Add(count, _tags);
        else
            _failureCounter?.Add(count);
    }

    /// <summary>Dispose and record duration.</summary>
    public void Dispose()
    {
        var elapsed = ElapsedMs;
        if (_tags != null)
            _durationHistogram?.Record(elapsed, _tags);
        else
            _durationHistogram?.Record(elapsed);
        _activity?.Dispose();
    }
}

/// <summary>
/// Factory methods for creating common diagnostics scopes.
/// </summary>
public static class DiagnosticsScopeFactory
{
    /// <summary>Create scope for EventStore append operations.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DiagnosticsScope EventStoreAppend(string streamId) => new(
        "Persistence.EventStore.Append",
        ActivityKind.Producer,
        CatgaDiagnostics.EventStoreAppends,
        CatgaDiagnostics.EventStoreFailures,
        CatgaDiagnostics.EventStoreAppendDuration,
        new KeyValuePair<string, object?>("stream", streamId));

    /// <summary>Create scope for EventStore read operations.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DiagnosticsScope EventStoreRead(string streamId) => new(
        "Persistence.EventStore.Read",
        ActivityKind.Internal,
        CatgaDiagnostics.EventStoreReads,
        CatgaDiagnostics.EventStoreFailures,
        CatgaDiagnostics.EventStoreReadDuration,
        new KeyValuePair<string, object?>("stream", streamId));

    /// <summary>Create scope for Inbox operations.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DiagnosticsScope InboxOperation(string operation) => new(
        $"Persistence.Inbox.{operation}",
        ActivityKind.Internal,
        CatgaDiagnostics.InboxProcessed,
        null,
        null);

    /// <summary>Create scope for Outbox operations.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DiagnosticsScope OutboxOperation(string operation) => new(
        $"Persistence.Outbox.{operation}",
        ActivityKind.Internal,
        CatgaDiagnostics.OutboxAdded,
        CatgaDiagnostics.OutboxFailed,
        null);

    /// <summary>Create scope for Idempotency operations.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DiagnosticsScope IdempotencyOperation(string operation) => new(
        $"Persistence.Idempotency.{operation}",
        ActivityKind.Internal);

    /// <summary>Create scope for distributed lock operations.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DiagnosticsScope LockOperation(string resource) => new(
        "DistributedLock.Acquire",
        ActivityKind.Internal,
        CatgaDiagnostics.LocksAcquired,
        CatgaDiagnostics.LocksFailed,
        CatgaDiagnostics.LockAcquireDuration,
        new KeyValuePair<string, object?>("resource", resource));
}
