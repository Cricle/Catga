using Catga.Abstractions;
using Catga.Core;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Catga.Observability;

/// <summary>
/// Minimal indirection for tracing/metrics. Core calls these no-ops unless enabled.
/// Keeps core free from direct ActivitySource/Counters usage.
/// </summary>
internal static class ObservabilityHooks
{
    // ========== State Management ==========

    private static volatile bool _enabled;

    public static void Enable() => _enabled = true;
    public static bool IsEnabled => _enabled;

    // ========== Commands/Requests Tracing ==========

    public static IDisposable? StartCommand(string requestType, IMessage? message)
    {
        if (!_enabled) return null;
        if (!CatgaActivitySource.Source.HasListeners()) return null;

        var activity = CatgaActivitySource.Source.StartActivity($"Command: {requestType}", ActivityKind.Internal);
        if (activity is null) return null;

        activity.SetTag(CatgaActivitySource.Tags.CatgaType, "command");
        activity.SetTag(CatgaActivitySource.Tags.RequestType, requestType);
        activity.SetTag(CatgaActivitySource.Tags.MessageType, requestType);

        if (message is not null)
        {
            activity.SetTag(CatgaActivitySource.Tags.MessageId, message.MessageId);
            if (message.CorrelationId.HasValue)
            {
                var correlationId = message.CorrelationId.Value;
                activity.SetTag(CatgaActivitySource.Tags.CorrelationId, correlationId);
                Span<char> buffer = stackalloc char[20];
                correlationId.TryFormat(buffer, out var written);
                activity.SetBaggage(CatgaActivitySource.Tags.CorrelationId, new string(buffer[..written]));
            }
        }

        return activity;
    }

    public static void RecordPipelineBehaviorCount(string requestType, int count)
    {
        if (!_enabled) return;
        var tag = new KeyValuePair<string, object?>("request_type", requestType);
        CatgaDiagnostics.PipelineBehaviorCount.Record(count, tag);
    }

    public static void RecordPipelineDuration(string requestType, double durationMs)
    {
        if (!_enabled) return;
        var tag = new KeyValuePair<string, object?>("request_type", requestType);
        CatgaDiagnostics.PipelineDuration.Record(durationMs, tag);
    }

    public static void RecordCommandResult(string requestType, bool success, double durationMs, IDisposable? handle)
    {
        if (!_enabled) return;
        var successValue = success ? "true" : "false";
        var tag1 = new KeyValuePair<string, object?>("request_type", requestType);
        var tag2 = new KeyValuePair<string, object?>("success", successValue);
        CatgaDiagnostics.CommandsExecuted.Add(1, tag1, tag2);

        var dTag = new KeyValuePair<string, object?>("request_type", requestType);
        CatgaDiagnostics.CommandDuration.Record(durationMs, dTag);
        if (handle is Activity a)
        {
            a.SetTag(CatgaActivitySource.Tags.Success, success);
            a.SetTag(CatgaActivitySource.Tags.Duration, durationMs);
            if (success) a.SetStatus(ActivityStatusCode.Ok);
        }
    }

    public static void RecordCommandError(string requestType, Exception ex, IDisposable? handle)
    {
        if (!_enabled) return;
        var tag1 = new KeyValuePair<string, object?>("request_type", requestType);
        var tag2 = new KeyValuePair<string, object?>("success", "false");
        CatgaDiagnostics.CommandsExecuted.Add(1, tag1, tag2);
        if (handle is Activity a)
        {
            a.SetStatus(ActivityStatusCode.Error, ex.Message);
            var typeName = ex.GetType().FullName ?? ex.GetType().Name;
            a.AddTag("exception.type", typeName);
            a.AddTag("exception.message", ex.Message);
        }
    }

    // ---- Events ----
    public static IDisposable? StartEventPublish(string eventType, IMessage? message)
    {
        if (!_enabled) return null;
        if (!CatgaActivitySource.Source.HasListeners()) return null;
        var activity = CatgaActivitySource.Source.StartActivity($"Event: {eventType}", ActivityKind.Producer);
        if (activity is null) return null;
        activity.SetTag(CatgaActivitySource.Tags.CatgaType, "event");
        activity.SetTag(CatgaActivitySource.Tags.EventType, eventType);
        activity.SetTag(CatgaActivitySource.Tags.EventName, eventType);
        activity.SetTag(CatgaActivitySource.Tags.MessageType, eventType);
        if (message is not null)
        {
            activity.SetTag(CatgaActivitySource.Tags.MessageId, message.MessageId);
            if (message.CorrelationId.HasValue)
                activity.SetTag(CatgaActivitySource.Tags.CorrelationId, message.CorrelationId.Value);
        }
        activity.AddActivityEvent(CatgaActivitySource.Events.EventPublished, ("event.type", eventType));
        return activity;
    }

    public static void RecordEventPublished(string eventType, int handlerCount)
    {
        if (!_enabled) return;
        Span<char> countBuffer = stackalloc char[10];
        handlerCount.TryFormat(countBuffer, out var charsWritten);
        var handlerCountStr = new string(countBuffer[..charsWritten]);
        var tag1 = new KeyValuePair<string, object?>("event_type", eventType);
        var tag2 = new KeyValuePair<string, object?>("handler_count", handlerCountStr);
        CatgaDiagnostics.EventsPublished.Add(1, tag1, tag2);
    }

    // ---- Mediator Auto-Batching ----
    public static void RecordMediatorBatchMetrics(int batchSize, int queueLength, double flushDurationMs)
    {
        if (!_enabled) return;
        CatgaDiagnostics.MediatorBatchSize.Record(batchSize);
        CatgaDiagnostics.MediatorBatchQueueLength.Record(queueLength);
        CatgaDiagnostics.MediatorBatchFlushDuration.Record(flushDurationMs);
    }

    public static void RecordMediatorBatchOverflow()
    {
        if (!_enabled) return;
        CatgaDiagnostics.MediatorBatchOverflow.Add(1);
    }
}
