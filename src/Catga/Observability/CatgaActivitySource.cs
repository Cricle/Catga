using System.Diagnostics;

namespace Catga.Observability;

/// <summary>
/// Centralized ActivitySource for Catga framework distributed tracing
/// Provides distributed tracing for all operations
/// </summary>
public static class CatgaActivitySource
{
    /// <summary>Activity source name for Catga framework</summary>
    public const string SourceName = "Catga.Framework";

    /// <summary>Activity source version</summary>
    public const string Version = "1.0.0";

    /// <summary>Shared ActivitySource instance</summary>
    public static readonly ActivitySource Source = new(SourceName, Version);

    /// <summary>Activity tag keys</summary>
    public static class Tags
    {
        // Catga type classification
        public const string CatgaType = "catga.type";  // command | event | query | aggregate

        // Message tags
        public const string MessageId = "catga.message.id";
        public const string MessageType = "catga.message.type";
        public const string CorrelationId = "catga.correlation_id";

        // Request/Response tags
        public const string RequestType = "catga.request.type";
        public const string ResponseType = "catga.response.type";
        public const string Success = "catga.success";
        public const string Error = "catga.error";
        public const string ErrorType = "catga.error.type";

        // Event tags
        public const string EventType = "catga.event.type";
        public const string EventId = "catga.event.id";
        public const string EventName = "catga.event.name";
        public const string HandlerType = "catga.handler.type";
        public const string HandlerCount = "catga.handler.count";

        // Performance tags
        public const string Duration = "catga.duration.ms";
        public const string QueueTime = "catga.queue_time.ms";

        // Aggregate tags
        public const string AggregateId = "catga.aggregate.id";
        public const string AggregateType = "catga.aggregate.type";
        public const string AggregateVersion = "catga.aggregate.version";
        public const string CommandResult = "catga.command.result";

        // OpenTelemetry semantic conventions (for transport layer)
        // See: https://opentelemetry.io/docs/specs/semconv/messaging/
        public const string MessagingMessageId = "messaging.message.id";
        public const string MessagingMessageType = "messaging.message.type";
        public const string MessagingDestination = "messaging.destination.name";
        public const string MessagingSystem = "messaging.system";
        public const string MessagingOperation = "messaging.operation";

        // Catga-specific tags
        public const string QoS = "catga.qos";
        public const string Handler = "catga.handler";
        public const string StreamId = "catga.stream_id";
        public const string EventCount = "catga.event_count";
    }

    /// <summary>Activity names</summary>
    public static class Activities
    {
        public const string SendCommand = "Catga.SendCommand";
        public const string SendQuery = "Catga.SendQuery";
        public const string PublishEvent = "Catga.PublishEvent";
        public const string HandleCommand = "Catga.HandleCommand";
        public const string HandleQuery = "Catga.HandleQuery";
        public const string HandleEvent = "Catga.HandleEvent";
        public const string Pipeline = "Catga.Pipeline";
        public const string Behavior = "Catga.Behavior";
    }

    /// <summary>Activity event names for timeline markers</summary>
    public static class Events
    {
        // State change events
        public const string StateChanged = "catga.state.changed";
        public const string AggregateLoaded = "catga.aggregate.loaded";
        public const string AggregateCreated = "catga.aggregate.created";

        // Event lifecycle
        public const string EventPublished = "catga.event.published";
        public const string EventReceived = "catga.event.received";
    }

    /// <summary>Mark activity as success with optional result</summary>
    public static void SetSuccess(this Activity activity, bool success, object? result = null)
    {
        activity.SetTag(Tags.Success, success);

        if (result != null)
        {
            activity.SetTag(Tags.CommandResult, result.ToString());
        }
    }

    /// <summary>Mark activity as failed with exception</summary>
    public static void SetError(this Activity activity, Exception exception)
    {
        activity.SetTag(Tags.Success, false);
        activity.SetTag(Tags.Error, exception.Message);
        activity.SetTag(Tags.ErrorType, exception.GetType().Name);
        activity.SetTag("exception.message", exception.Message);
        activity.SetTag("exception.type", exception.GetType().FullName);
        activity.SetTag("exception.stacktrace", exception.StackTrace);
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
    }

    /// <summary>Add event to activity timeline</summary>
    public static void AddActivityEvent(this Activity activity, string name, params (string key, object? value)[] tags)
    {
        var activityTags = new ActivityTagsCollection();
        foreach (var (key, value) in tags)
        {
            activityTags[key] = value;
        }
        activity.AddEvent(new ActivityEvent(name, tags: activityTags));
    }
}
