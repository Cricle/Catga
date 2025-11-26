using System.Diagnostics;
using System.Diagnostics.Metrics;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Observability;

/// <summary>
/// Minimal indirection for tracing/metrics. Core calls these no-ops unless enabled.
/// Keeps core free from direct ActivitySource/Counters usage.
/// </summary>
public static class ObservabilityHooks
{
    private static volatile bool _enabled;

    public static void Enable() => _enabled = true;
    public static bool IsEnabled => _enabled;

    // ---- Commands/Requests ----
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
#if NET8_0_OR_GREATER
        CatgaDiagnostics.PipelineBehaviorCount.Record(count,
            new KeyValuePair<string, object?>("request_type", requestType));
#else
        var _tags = new TagList { { "request_type", requestType } };
        CatgaDiagnostics.PipelineBehaviorCount.Record(count, _tags);
#endif
    }

    public static void RecordPipelineDuration(string requestType, double durationMs)
    {
        if (!_enabled) return;
#if NET8_0_OR_GREATER
        CatgaDiagnostics.PipelineDuration.Record(durationMs,
            new KeyValuePair<string, object?>("request_type", requestType));
#else
        var _tags = new TagList { { "request_type", requestType } };
        CatgaDiagnostics.PipelineDuration.Record(durationMs, _tags);
#endif
    }

    public static void RecordCommandResult(string requestType, bool success, double durationMs, IDisposable? handle)
    {
        if (!_enabled) return;
        var successValue = success ? "true" : "false";
#if NET8_0_OR_GREATER
        CatgaDiagnostics.CommandsExecuted.Add(1,
            new KeyValuePair<string, object?>("request_type", requestType),
            new KeyValuePair<string, object?>("success", successValue));
        CatgaDiagnostics.CommandDuration.Record(durationMs,
            new KeyValuePair<string, object?>("request_type", requestType));
#else
        var _tags_executed = new TagList { { "request_type", requestType }, { "success", successValue } };
        var _tags_duration = new TagList { { "request_type", requestType } };
        CatgaDiagnostics.CommandsExecuted.Add(1, _tags_executed);
        CatgaDiagnostics.CommandDuration.Record(durationMs, _tags_duration);
#endif
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
#if NET8_0_OR_GREATER
        CatgaDiagnostics.CommandsExecuted.Add(1,
            new KeyValuePair<string, object?>("request_type", requestType),
            new KeyValuePair<string, object?>("success", "false"));
#else
        var _tags = new TagList { { "request_type", requestType }, { "success", "false" } };
        CatgaDiagnostics.CommandsExecuted.Add(1, _tags);
#endif
        if (handle is Activity a)
        {
            a.SetStatus(ActivityStatusCode.Error, ex.Message);
            a.AddTag("exception.type", ExceptionTypeCache.GetFullTypeName(ex));
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
#if NET8_0_OR_GREATER
        CatgaDiagnostics.EventsPublished.Add(1,
            new KeyValuePair<string, object?>("event_type", eventType),
            new KeyValuePair<string, object?>("handler_count", handlerCountStr));
#else
        var _tags_event = new TagList { { "event_type", eventType }, { "handler_count", handlerCountStr } };
        CatgaDiagnostics.EventsPublished.Add(1, _tags_event);
#endif
    }
}
