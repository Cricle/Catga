using Catga.Abstractions;
using System.Diagnostics;

namespace Catga.Observability;

/// <summary>Minimal hooks for tracing/metrics. Core calls these no-ops unless enabled.</summary>
internal static class ObservabilityHooks
{
    private static volatile bool _enabled;

    public static void Enable() => _enabled = true;
    public static bool IsEnabled => _enabled;

    public static IDisposable? StartCommand(string requestType, IMessage? message)
    {
        if (!_enabled || !CatgaActivitySource.Source.HasListeners()) return null;

        var activity = CatgaActivitySource.Source.StartActivity($"Command: {requestType}", ActivityKind.Internal);
        if (activity is null) return null;

        activity.SetTag(CatgaActivitySource.Tags.CatgaType, "command");
        activity.SetTag(CatgaActivitySource.Tags.RequestType, requestType);

        if (message is not null)
        {
            activity.SetTag(CatgaActivitySource.Tags.MessageId, message.MessageId);
            if (message.CorrelationId.HasValue)
                activity.SetTag(CatgaActivitySource.Tags.CorrelationId, message.CorrelationId.Value);
        }

        return activity;
    }

    public static void RecordPipelineBehaviorCount(string requestType, int count)
    {
        if (!_enabled) return;
        CatgaDiagnostics.PipelineBehaviorCount.Record(count, new KeyValuePair<string, object?>("request_type", requestType));
    }

    public static void RecordPipelineDuration(string requestType, double durationMs)
    {
        if (!_enabled) return;
        CatgaDiagnostics.PipelineDuration.Record(durationMs, new KeyValuePair<string, object?>("request_type", requestType));
    }

    public static void RecordCommandResult(string requestType, bool success, double durationMs, IDisposable? handle)
    {
        if (!_enabled) return;
        CatgaDiagnostics.CommandsExecuted.Add(1,
            new KeyValuePair<string, object?>("request_type", requestType),
            new KeyValuePair<string, object?>("success", success ? "true" : "false"));
        CatgaDiagnostics.CommandDuration.Record(durationMs, new KeyValuePair<string, object?>("request_type", requestType));

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
        CatgaDiagnostics.CommandsExecuted.Add(1,
            new KeyValuePair<string, object?>("request_type", requestType),
            new KeyValuePair<string, object?>("success", "false"));

        if (handle is Activity a)
        {
            a.SetStatus(ActivityStatusCode.Error, ex.Message);
            a.AddTag("exception.type", ex.GetType().FullName ?? ex.GetType().Name);
            a.AddTag("exception.message", ex.Message);
        }
    }

    public static IDisposable? StartEventPublish(string eventType, IMessage? message)
    {
        if (!_enabled || !CatgaActivitySource.Source.HasListeners()) return null;

        var activity = CatgaActivitySource.Source.StartActivity($"Event: {eventType}", ActivityKind.Producer);
        if (activity is null) return null;

        activity.SetTag(CatgaActivitySource.Tags.CatgaType, "event");
        activity.SetTag(CatgaActivitySource.Tags.EventType, eventType);

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
        CatgaDiagnostics.EventsPublished.Add(1,
            new KeyValuePair<string, object?>("event_type", eventType),
            new KeyValuePair<string, object?>("handler_count", handlerCount.ToString()));
    }

    public static void RecordMediatorBatchMetrics(int batchSize, int queueLength, double flushDurationMs)
    {
        if (!_enabled) return;
        CatgaDiagnostics.MediatorBatchSize.Record(batchSize);
        CatgaDiagnostics.MediatorBatchFlushDuration.Record(flushDurationMs);
    }

    public static void RecordMediatorBatchOverflow()
    {
        if (!_enabled) return;
        CatgaDiagnostics.MediatorBatchOverflow.Add(1);
    }
}
