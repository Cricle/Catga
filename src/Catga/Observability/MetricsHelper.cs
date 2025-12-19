using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Catga.Observability;

/// <summary>
/// Helper class for recording metrics with consistent patterns.
/// Reduces boilerplate code across persistence and transport layers.
/// </summary>
public static class MetricsHelper
{
    #region Stopwatch Helpers

    /// <summary>Get elapsed milliseconds from a Stopwatch timestamp</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double GetElapsedMs(long startTimestamp)
        => (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;

    /// <summary>Start a new timestamp for duration measurement</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long StartTimestamp() => Stopwatch.GetTimestamp();

    #endregion

    #region Activity Helpers

    /// <summary>Start an activity with common tags</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
        => CatgaDiagnostics.ActivitySource.StartActivity(name, kind);

    /// <summary>Start a persistence operation activity</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Activity? StartPersistenceActivity(string store, string operation)
        => CatgaDiagnostics.ActivitySource.StartActivity($"Persistence.{store}.{operation}", ActivityKind.Internal);

    /// <summary>Set activity success status with duration</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetActivitySuccess(Activity? activity, long startTimestamp)
    {
        if (activity == null) return;
        activity.SetTag(CatgaActivitySource.Tags.Success, true);
        activity.SetTag(CatgaActivitySource.Tags.Duration, GetElapsedMs(startTimestamp));
        activity.SetStatus(ActivityStatusCode.Ok);
    }

    /// <summary>Set activity error status</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetActivityError(Activity? activity, Exception ex)
    {
        if (activity == null) return;
        activity.SetTag(CatgaActivitySource.Tags.Success, false);
        activity.SetTag(CatgaActivitySource.Tags.Error, ex.Message);
        activity.SetStatus(ActivityStatusCode.Error, ex.Message);
    }

    #endregion

    #region EventStore Metrics

    /// <summary>Record event store append metrics</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordEventStoreAppend(int eventCount, long startTimestamp)
    {
        CatgaDiagnostics.EventStoreAppends.Add(eventCount);
        CatgaDiagnostics.EventStoreAppendDuration.Record(GetElapsedMs(startTimestamp));
    }

    /// <summary>Record event store read metrics</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordEventStoreRead(long startTimestamp)
    {
        CatgaDiagnostics.EventStoreReads.Add(1);
        CatgaDiagnostics.EventStoreReadDuration.Record(GetElapsedMs(startTimestamp));
    }

    /// <summary>Record event store failure</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordEventStoreFailure()
        => CatgaDiagnostics.EventStoreFailures.Add(1);

    #endregion

    #region Inbox/Outbox Metrics

    /// <summary>Record inbox message processed</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordInboxProcessed()
        => CatgaDiagnostics.InboxProcessed.Add(1);

    /// <summary>Record inbox lock acquired</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordInboxLockAcquired()
        => CatgaDiagnostics.InboxLocksAcquired.Add(1);

    /// <summary>Record inbox lock released</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordInboxLockReleased()
        => CatgaDiagnostics.InboxLocksReleased.Add(1);

    /// <summary>Record outbox message added</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordOutboxAdded()
        => CatgaDiagnostics.OutboxAdded.Add(1);

    /// <summary>Record outbox message published</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordOutboxPublished()
        => CatgaDiagnostics.OutboxPublished.Add(1);

    /// <summary>Record outbox failure</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordOutboxFailed()
        => CatgaDiagnostics.OutboxFailed.Add(1);

    #endregion

    #region Idempotency Metrics

    /// <summary>Record idempotency cache hit</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordIdempotencyHit()
        => CatgaDiagnostics.IdempotencyHits.Add(1);

    /// <summary>Record idempotency cache miss</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordIdempotencyMiss()
        => CatgaDiagnostics.IdempotencyMisses.Add(1);

    /// <summary>Record idempotency result based on condition</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordIdempotency(bool hit)
    {
        if (hit) CatgaDiagnostics.IdempotencyHits.Add(1);
        else CatgaDiagnostics.IdempotencyMisses.Add(1);
    }

    #endregion

    #region Lock Metrics

    /// <summary>Record lock acquired with duration</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordLockAcquired(long startTimestamp)
    {
        CatgaDiagnostics.LocksAcquired.Add(1);
        CatgaDiagnostics.LockAcquireDuration.Record(GetElapsedMs(startTimestamp));
    }

    /// <summary>Record lock acquisition failed</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordLockFailed()
        => CatgaDiagnostics.LocksFailed.Add(1);

    #endregion

    #region Dead Letter Metrics

    /// <summary>Record dead letter message</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordDeadLetter()
        => CatgaDiagnostics.DeadLetters.Add(1);

    #endregion

    #region Flow Metrics

    /// <summary>Record flow started</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordFlowStarted()
    {
        CatgaDiagnostics.FlowsStarted.Add(1);
        CatgaDiagnostics.IncrementActiveFlows();
    }

    /// <summary>Record flow completed with duration</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordFlowCompleted(long startTimestamp)
    {
        CatgaDiagnostics.FlowsCompleted.Add(1);
        CatgaDiagnostics.DecrementActiveFlows();
        CatgaDiagnostics.FlowDuration.Record(GetElapsedMs(startTimestamp));
    }

    /// <summary>Record flow failed</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordFlowFailed()
    {
        CatgaDiagnostics.FlowsFailed.Add(1);
        CatgaDiagnostics.DecrementActiveFlows();
    }

    /// <summary>Record step executed</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordStepExecuted(bool success, long startTimestamp)
    {
        CatgaDiagnostics.StepsExecuted.Add(1);
        if (success) CatgaDiagnostics.StepsSucceeded.Add(1);
        else CatgaDiagnostics.StepsFailed.Add(1);
        CatgaDiagnostics.StepDuration.Record(GetElapsedMs(startTimestamp));
    }

    #endregion

    #region Resilience Metrics

    /// <summary>Record resilience retry</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordResilienceRetry()
        => CatgaDiagnostics.ResilienceRetries.Add(1);

    /// <summary>Record circuit breaker opened</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordCircuitOpened()
        => CatgaDiagnostics.ResilienceCircuitOpened.Add(1);

    #endregion

    #region Message Metrics

    /// <summary>Record message published</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordMessagePublished(string component, string messageType, string destination)
    {
        CatgaDiagnostics.MessagesPublished.Add(1,
            new KeyValuePair<string, object?>("component", component),
            new KeyValuePair<string, object?>("message_type", messageType),
            new KeyValuePair<string, object?>("destination", destination));
    }

    /// <summary>Record message failed</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordMessageFailed(string component, string destination, string? reason = null)
    {
        CatgaDiagnostics.MessagesFailed.Add(1,
            new KeyValuePair<string, object?>("component", component),
            new KeyValuePair<string, object?>("destination", destination),
            new KeyValuePair<string, object?>("reason", reason ?? "unknown"));
    }

    #endregion
}
