using System.Diagnostics;
using Catga.Transport;

namespace Catga.Observability;

/// <summary>
/// Propagates W3C Trace Context across message boundaries
/// Implements https://www.w3.org/TR/trace-context/
/// </summary>
public static class TraceContextPropagator
{
    /// <summary>
    /// W3C Trace Context header name
    /// </summary>
    public const string TraceParentKey = "traceparent";

    /// <summary>
    /// W3C Trace State header name
    /// </summary>
    public const string TraceStateKey = "tracestate";

    /// <summary>
    /// Inject current trace context into message metadata
    /// </summary>
    /// <param name="context">Transport context to inject into</param>
    /// <returns>Updated transport context with trace information</returns>
    public static TransportContext Inject(TransportContext context)
    {
        var activity = Activity.Current;
        if (activity == null) return context;

        var metadata = context.Metadata ?? new Dictionary<string, string>();

        // Inject traceparent (required)
        if (!string.IsNullOrEmpty(activity.Id))
        {
            metadata[TraceParentKey] = activity.Id;
        }

        // Inject tracestate (optional)
        if (!string.IsNullOrEmpty(activity.TraceStateString))
        {
            metadata[TraceStateKey] = activity.TraceStateString;
        }

        return context with { Metadata = metadata };
    }

    /// <summary>
    /// Extract trace context from message metadata and create a linked activity
    /// </summary>
    /// <param name="context">Transport context to extract from</param>
    /// <param name="activityName">Name for the new activity</param>
    /// <param name="kind">Activity kind (default: Consumer)</param>
    /// <returns>New activity if trace context was found, null otherwise</returns>
    public static Activity? Extract(
        TransportContext? context,
        string activityName,
        ActivityKind kind = ActivityKind.Consumer)
    {
        if (string.IsNullOrWhiteSpace(activityName))
            throw new ArgumentException("Activity name cannot be null or whitespace", nameof(activityName));

        if (context?.Metadata == null)
        {
            // No context, create activity without parent
            return CatgaActivitySource.Source.StartActivity(activityName, kind);
        }

        // Try to extract traceparent
        if (context.Value.Metadata.TryGetValue(TraceParentKey, out var traceParent) &&
            !string.IsNullOrEmpty(traceParent))
        {
            // Create activity with parent context
            var activity = CatgaActivitySource.Source.StartActivity(
                activityName,
                kind,
                traceParent);

            if (activity != null)
            {
                // Add tracestate if present
                if (context.Value.Metadata.TryGetValue(TraceStateKey, out var traceState) &&
                    !string.IsNullOrEmpty(traceState))
                {
                    activity.TraceStateString = traceState;
                }

                return activity;
            }
        }

        // Fallback: create activity without parent
        return CatgaActivitySource.Source.StartActivity(activityName, kind);
    }

    /// <summary>
    /// Add common message tags to current activity
    /// </summary>
    /// <param name="activity">Activity to add tags to</param>
    /// <param name="messageId">Message ID</param>
    /// <param name="messageType">Message type name</param>
    /// <param name="operation">Operation type</param>
    public static void AddMessageTags(
        Activity? activity,
        string? messageId,
        string? messageType,
        string? operation)
    {
        if (activity == null) return;

        if (!string.IsNullOrEmpty(messageId))
            activity.SetTag(CatgaActivitySource.Tags.MessagingMessageId, messageId);

        if (!string.IsNullOrEmpty(messageType))
            activity.SetTag(CatgaActivitySource.Tags.MessagingMessageType, messageType);

        if (!string.IsNullOrEmpty(operation))
            activity.SetTag(CatgaActivitySource.Tags.MessagingOperation, operation);
    }

    /// <summary>
    /// Record exception in current activity
    /// </summary>
    /// <param name="activity">Activity to record exception in</param>
    /// <param name="exception">Exception to record</param>
    public static void RecordException(Activity? activity, Exception exception)
    {
        if (activity == null || exception == null) return;

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag(CatgaActivitySource.Tags.Error, exception.Message);
        activity.SetTag(CatgaActivitySource.Tags.ErrorType, exception.GetType().Name);

        // Record exception event following OpenTelemetry spec
        var tags = new ActivityTagsCollection
        {
            { "exception.type", exception.GetType().FullName },
            { "exception.message", exception.Message },
            { "exception.stacktrace", exception.StackTrace }
        };

        activity.AddEvent(new ActivityEvent("exception", tags: tags));
    }
}

