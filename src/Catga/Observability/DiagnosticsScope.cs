using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;

namespace Catga.Observability;

/// <summary>Scoped diagnostics helper that tracks duration and handles errors.</summary>
public readonly struct DiagnosticsScope(
    string activityName,
    Counter<long>? successCounter = null,
    Counter<long>? failureCounter = null,
    Histogram<double>? durationHistogram = null,
    string? tagKey = null,
    string? tagValue = null) : IDisposable
{
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private readonly Activity? _activity = CatgaDiagnostics.ActivitySource.StartActivity(activityName, ActivityKind.Internal);

    public Activity? Activity => _activity;
    public double ElapsedMs => Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetError(Exception ex)
    {
        _activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        _activity?.SetTag("error.type", ex.GetType().Name);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordSuccess(long count = 1)
    {
        if (tagKey != null && tagValue != null)
            successCounter?.Add(count, new KeyValuePair<string, object?>(tagKey, tagValue));
        else
            successCounter?.Add(count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordFailure(long count = 1)
    {
        if (tagKey != null && tagValue != null)
            failureCounter?.Add(count, new KeyValuePair<string, object?>(tagKey, tagValue));
        else
            failureCounter?.Add(count);
    }

    public void Dispose()
    {
        var elapsed = ElapsedMs;
        if (tagKey != null && tagValue != null)
            durationHistogram?.Record(elapsed, new KeyValuePair<string, object?>(tagKey, tagValue));
        else
            durationHistogram?.Record(elapsed);
        _activity?.Dispose();
    }
}

/// <summary>Factory methods for common diagnostics scopes.</summary>
public static class DiagnosticsScopeFactory
{
    public static DiagnosticsScope EventStoreAppend(string streamId) => new(
        "Persistence.EventStore.Append",
        CatgaDiagnostics.EventStoreAppends,
        CatgaDiagnostics.EventStoreFailures,
        CatgaDiagnostics.EventStoreAppendDuration,
        "stream", streamId);

    public static DiagnosticsScope EventStoreRead(string streamId) => new(
        "Persistence.EventStore.Read",
        CatgaDiagnostics.EventStoreReads,
        CatgaDiagnostics.EventStoreFailures,
        CatgaDiagnostics.EventStoreReadDuration,
        "stream", streamId);

    public static DiagnosticsScope InboxOperation(string operation) => new(
        $"Persistence.Inbox.{operation}",
        CatgaDiagnostics.InboxProcessed);

    public static DiagnosticsScope OutboxOperation(string operation) => new(
        $"Persistence.Outbox.{operation}",
        CatgaDiagnostics.OutboxAdded,
        CatgaDiagnostics.OutboxFailed);

    public static DiagnosticsScope LockOperation(string resource) => new(
        "DistributedLock.Acquire",
        CatgaDiagnostics.LocksAcquired,
        CatgaDiagnostics.LocksFailed,
        CatgaDiagnostics.LockAcquireDuration,
        "resource", resource);
}
