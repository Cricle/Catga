using System.Diagnostics;

namespace Catga.Observability;

/// <summary>
/// Centralized ActivitySource for Catga framework distributed tracing
/// Provides distributed tracing for all operations
/// </summary>
internal static class CatgaActivitySource
{
    // ========== Core Activity Source ==========

    /// <summary>Activity source name for Catga framework</summary>
    public const string SourceName = "Catga.Framework";

    /// <summary>Activity source version</summary>
    public const string Version = "1.0.0";

    /// <summary>Shared ActivitySource instance</summary>
    public static readonly ActivitySource Source = new(SourceName, Version);

    // ========== Activity Tag Keys ==========

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
        public const string PipelineBehaviorCount = "catga.pipeline.behavior_count";

        // Distributed Lock tags
        public const string LockResource = "catga.lock.resource";
        public const string LockId = "catga.lock.id";
        public const string LockExpiry = "catga.lock.expiry_ms";
        public const string LockWaitTimeout = "catga.lock.wait_timeout_ms";

        // Leader Election tags
        public const string ElectionId = "catga.election.id";
        public const string LeaderNodeId = "catga.leader.node_id";
        public const string LeaderLeaseDuration = "catga.leader.lease_duration_ms";

        // Rate Limiter tags
        public const string RateLimitKey = "catga.ratelimit.key";
        public const string RateLimitPermits = "catga.ratelimit.permits";
        public const string RateLimitRemaining = "catga.ratelimit.remaining";
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

        // Transport events (transport-agnostic names)
        public const string TransportPublishEnqueued = "Transport.Publish.Enqueued";
        public const string TransportPublishSent = "Transport.Publish.Sent";
        public const string TransportPublishFailed = "Transport.Publish.Failed";
        public const string TransportReceiveEmpty = "Transport.Receive.Empty";
        public const string TransportReceiveDeserialized = "Transport.Receive.Deserialized";
        public const string TransportReceiveDroppedDuplicate = "Transport.Receive.DroppedDuplicate";
        public const string TransportReceiveHandler = "Transport.Receive.Handler";
        public const string TransportReceiveProcessed = "Transport.Receive.Processed";
        public const string TransportBatchItemSent = "Transport.Batch.ItemSent";
        public const string TransportBatchItemFailed = "Transport.Batch.ItemFailed";

        // InMemory transport events (part of core)
        public const string InMemoryPublishSent = "InMemory.Publish.Sent";
        public const string InMemoryReceiveHandler = "InMemory.Receive.Handler";
        public const string InMemoryReceiveProcessed = "InMemory.Receive.Processed";

        public const string OutboxSerialized = "Outbox.Serialized";
        public const string OutboxSaved = "Outbox.Saved";
        public const string OutboxPublished = "Outbox.Published";

        // Outbox persistence (stores)
        public const string OutboxAdded = "Outbox.Added";
        public const string OutboxGetPendingItem = "Outbox.GetPending.Item";
        public const string OutboxGetPendingNotFound = "Outbox.GetPending.NotFound";
        public const string OutboxGetPendingDone = "Outbox.GetPending.Done";
        public const string OutboxGetPendingEmpty = "Outbox.GetPending.Empty";
        public const string OutboxMarkPublished = "Outbox.MarkPublished";
        public const string OutboxMarkPublishedNotFound = "Outbox.MarkPublished.NotFound";
        public const string OutboxMarkFailedUpdated = "Outbox.MarkFailed.Updated";
        public const string OutboxMarkFailedNotFound = "Outbox.MarkFailed.NotFound";
        public const string OutboxMarkFailedFinal = "Outbox.MarkFailed.Final";
        public const string OutboxMarkFailedRetry = "Outbox.MarkFailed.Retry";
        public const string OutboxCleanup = "Outbox.Cleanup";

        // Inbox persistence
        public const string InboxTryLockOk = "Inbox.TryLock.Ok";
        public const string InboxTryLockFailed = "Inbox.TryLock.Failed";
        public const string InboxMarkProcessed = "Inbox.MarkProcessed";
        public const string InboxHasBeenProcessed = "Inbox.HasBeenProcessed";
        public const string InboxGetProcessedResultHit = "Inbox.GetProcessedResult.Hit";
        public const string InboxGetProcessedResultMiss = "Inbox.GetProcessedResult.Miss";
        public const string InboxReleaseLock = "Inbox.ReleaseLock";
        public const string InboxDeleteProcessedNoop = "Inbox.DeleteProcessed.Noop";

        // EventStore
        public const string EventStoreAppendConcurrencyMismatch = "EventStore.Append.ConcurrencyMismatch";
        public const string EventStoreAppendItem = "EventStore.Append.Item";
        public const string EventStoreAppendDone = "EventStore.Append.Done";
        public const string EventStoreReadDeserialized = "EventStore.Read.Deserialized";
        public const string EventStoreReadItem = "EventStore.Read.Item";
        public const string EventStoreReadDone = "EventStore.Read.Done";
        public const string EventStoreGetVersionNone = "EventStore.GetVersion.None";
        public const string EventStoreGetVersionNotFound = "EventStore.GetVersion.NotFound";

        // Pipeline
        public const string PipelineBehaviorStart = "Pipeline.Behavior.Start";
        public const string PipelineBehaviorDone = "Pipeline.Behavior.Done";
        public const string PipelineHandlerStart = "Pipeline.Handler.Start";
        public const string PipelineHandlerDone = "Pipeline.Handler.Done";

        // Resilience
        public const string ResilienceBulkheadRejected = "resilience.bulkhead.rejected";
        public const string ResilienceCircuitOpen = "resilience.circuit.open";
        public const string ResilienceCircuitHalfOpen = "resilience.circuit.halfopen";
        public const string ResilienceCircuitClosed = "resilience.circuit.closed";
        public const string ResilienceTimeout = "resilience.timeout";
        public const string ResilienceRetry = "resilience.retry";

        // Distributed Lock
        public const string LockTryAcquire = "Lock.TryAcquire";
        public const string LockAcquired = "Lock.Acquired";
        public const string LockAcquireFailed = "Lock.Acquire.Failed";
        public const string LockAcquireTimeout = "Lock.Acquire.Timeout";
        public const string LockReleased = "Lock.Released";
        public const string LockExtended = "Lock.Extended";
        public const string LockExtendFailed = "Lock.Extend.Failed";

        // Leader Election
        public const string LeaderTryAcquire = "Leader.TryAcquire";
        public const string LeaderAcquired = "Leader.Acquired";
        public const string LeaderAcquireFailed = "Leader.Acquire.Failed";
        public const string LeaderAcquireTimeout = "Leader.Acquire.Timeout";
        public const string LeaderResigned = "Leader.Resigned";
        public const string LeaderExtended = "Leader.Extended";
        public const string LeaderLost = "Leader.Lost";

        // Rate Limiter
        public const string RateLimitTryAcquire = "RateLimit.TryAcquire";
        public const string RateLimitAcquired = "RateLimit.Acquired";
        public const string RateLimitRejected = "RateLimit.Rejected";
        public const string RateLimitWait = "RateLimit.Wait";
        public const string RateLimitTimeout = "RateLimit.Timeout";
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
        var category = GetErrorCategory(exception);
        activity.SetTag("catga.error.category", category);
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

    private static string GetErrorCategory(Exception ex)
    {
        if (ex is OperationCanceledException) return "cancellation";
        if (ex is TimeoutException) return "timeout";
        if (ex is UnauthorizedAccessException || ex.GetType().Name.IndexOf("Authorization", StringComparison.OrdinalIgnoreCase) >= 0) return "authorization";

        var typeName = ex.GetType().FullName ?? ex.GetType().Name;
        if (typeName.IndexOf("Concurrency", StringComparison.OrdinalIgnoreCase) >= 0) return "concurrency";
        if (typeName.IndexOf("Json", StringComparison.OrdinalIgnoreCase) >= 0 ||
            typeName.IndexOf("Serialization", StringComparison.OrdinalIgnoreCase) >= 0 ||
            typeName.IndexOf("MemoryPack", StringComparison.OrdinalIgnoreCase) >= 0) return "serialization";
        if (typeName.IndexOf("HttpRequest", StringComparison.OrdinalIgnoreCase) >= 0 ||
            typeName.IndexOf("Socket", StringComparison.OrdinalIgnoreCase) >= 0 ||
            typeName.IndexOf("Network", StringComparison.OrdinalIgnoreCase) >= 0) return "network";
        if (typeName.IndexOf("Persistence", StringComparison.OrdinalIgnoreCase) >= 0 ||
            typeName.IndexOf("Store", StringComparison.OrdinalIgnoreCase) >= 0) return "persistence";
        if (typeName.IndexOf("Transport", StringComparison.OrdinalIgnoreCase) >= 0) return "transport";
        if (ex is ArgumentException || ex is FormatException) return "validation";
        return "unknown";
    }
}
