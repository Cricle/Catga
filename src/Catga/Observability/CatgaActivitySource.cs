using System.Diagnostics;

namespace Catga.Observability;

/// <summary>Centralized ActivitySource for Catga framework distributed tracing</summary>
internal static class CatgaActivitySource
{
    public const string SourceName = "Catga";
    public static readonly ActivitySource Source = new(SourceName, "1.0.0");

    /// <summary>Activity tag keys</summary>
    public static class Tags
    {
        // Core
        public const string CatgaType = "catga.type";
        public const string MessageId = "catga.message.id";
        public const string MessageType = "catga.message.type";
        public const string CorrelationId = "catga.correlation_id";
        public const string RequestType = "catga.request.type";
        public const string Success = "catga.success";
        public const string Error = "catga.error";
        public const string ErrorType = "catga.error.type";
        public const string Duration = "catga.duration.ms";

        // Event
        public const string EventType = "catga.event.type";
        public const string HandlerType = "catga.handler.type";
        public const string HandlerCount = "catga.handler.count";

        // Aggregate
        public const string AggregateId = "catga.aggregate.id";
        public const string AggregateType = "catga.aggregate.type";
        public const string AggregateVersion = "catga.aggregate.version";
        public const string CommandResult = "catga.command.result";
        public const string StreamId = "catga.stream_id";
        public const string EventCount = "catga.event_count";

        // OpenTelemetry semantic conventions
        public const string MessagingMessageId = "messaging.message.id";
        public const string MessagingDestination = "messaging.destination.name";
        public const string MessagingSystem = "messaging.system";

        // Lock
        public const string LockResource = "catga.lock.resource";

        // Flow
        public const string FlowName = "catga.flow.name";
        public const string FlowId = "catga.flow.id";
        public const string FlowStatus = "catga.flow.status";
        public const string StepIndex = "catga.flow.step.index";
        public const string StepType = "catga.flow.step.type";
        public const string StepTag = "catga.flow.step.tag";
        public const string StepStatus = "catga.flow.step.status";
    }

    // Backwards compatibility aliases
    public static class FlowTags
    {
        public const string FlowName = Tags.FlowName;
        public const string FlowId = Tags.FlowId;
        public const string FlowStatus = Tags.FlowStatus;
        public const string StepIndex = Tags.StepIndex;
        public const string StepType = Tags.StepType;
        public const string StepTag = Tags.StepTag;
        public const string StepStatus = Tags.StepStatus;
        public const string Error = Tags.Error;
        public const string Duration = Tags.Duration;
    }

    public static class FlowEvents
    {
        public const string FlowStarted = Events.FlowStarted;
        public const string FlowCompleted = Events.FlowCompleted;
        public const string FlowFailed = Events.FlowFailed;
        public const string StepStarted = Events.StepStarted;
        public const string StepCompleted = Events.StepCompleted;
        public const string StepFailed = Events.StepFailed;
    }

    /// <summary>Activity event names</summary>
    public static class Events
    {
        // Aggregate
        public const string AggregateLoaded = "catga.aggregate.loaded";
        public const string AggregateCreated = "catga.aggregate.created";

        // Event
        public const string EventPublished = "catga.event.published";

        // Outbox
        public const string OutboxSaved = "Outbox.Saved";
        public const string OutboxPublished = "Outbox.Published";

        // Inbox
        public const string InboxTryLockOk = "Inbox.TryLock.Ok";
        public const string InboxMarkProcessed = "Inbox.MarkProcessed";

        // EventStore
        public const string EventStoreAppendDone = "EventStore.Append.Done";
        public const string EventStoreReadDone = "EventStore.Read.Done";

        // Pipeline
        public const string PipelineBehaviorStart = "Pipeline.Behavior.Start";
        public const string PipelineBehaviorDone = "Pipeline.Behavior.Done";

        // Lock
        public const string LockAcquired = "Lock.Acquired";
        public const string LockReleased = "Lock.Released";

        // Flow
        public const string FlowStarted = "catga.flow.started";
        public const string FlowCompleted = "catga.flow.completed";
        public const string FlowFailed = "catga.flow.failed";
        public const string StepStarted = "catga.flow.step.started";
        public const string StepCompleted = "catga.flow.step.completed";
        public const string StepFailed = "catga.flow.step.failed";
    }

    /// <summary>Mark activity as failed with exception</summary>
    public static void SetError(this Activity activity, Exception exception)
    {
        activity.SetTag(Tags.Success, false);
        activity.SetTag(Tags.Error, exception.Message);
        activity.SetTag(Tags.ErrorType, exception.GetType().Name);
        activity.SetTag("exception.message", exception.Message);
        activity.SetTag("exception.type", exception.GetType().FullName);
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
    }

    /// <summary>Add event to activity timeline</summary>
    public static void AddActivityEvent(this Activity activity, string name, params (string key, object? value)[] tags)
    {
        var activityTags = new ActivityTagsCollection();
        foreach (var (key, value) in tags)
            activityTags[key] = value;
        activity.AddEvent(new ActivityEvent(name, tags: activityTags));
    }

    /// <summary>Start a flow execution activity</summary>
    public static Activity? StartFlowActivity(string flowName, string? flowId = null)
    {
        var activity = Source.StartActivity(flowName, ActivityKind.Internal);
        activity?.SetTag(Tags.FlowName, flowName);
        if (flowId != null) activity?.SetTag(Tags.FlowId, flowId);
        return activity;
    }

    /// <summary>Start a step execution activity</summary>
    public static Activity? StartStepActivity(string flowName, int stepIndex, string stepType)
    {
        var activity = Source.StartActivity($"{flowName}.Step{stepIndex}", ActivityKind.Internal);
        activity?.SetTag(Tags.FlowName, flowName);
        activity?.SetTag(Tags.StepIndex, stepIndex);
        activity?.SetTag(Tags.StepType, stepType);
        return activity;
    }
}
