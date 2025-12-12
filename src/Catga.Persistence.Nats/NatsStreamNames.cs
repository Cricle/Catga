namespace Catga.Persistence.Nats;

/// <summary>
/// Centralized NATS JetStream stream names for all stores (DRY principle).
/// </summary>
public static class NatsStreamNames
{
    /// <summary>Default stream name prefix.</summary>
    public const string DefaultPrefix = "CATGA";

    /// <summary>Idempotency store stream name.</summary>
    public const string Idempotency = "CATGA_IDEMPOTENCY";

    /// <summary>Dead letter queue stream name.</summary>
    public const string DeadLetterQueue = "CATGA_DLQ";

    /// <summary>Inbox store stream name.</summary>
    public const string Inbox = "CATGA_INBOX";

    /// <summary>Outbox store stream name.</summary>
    public const string Outbox = "CATGA_OUTBOX";

    /// <summary>Event store stream name.</summary>
    public const string Events = "CATGA_EVENTS";

    /// <summary>Snapshot store stream name.</summary>
    public const string Snapshots = "CATGA_SNAPSHOTS";

    /// <summary>Audit log stream name.</summary>
    public const string AuditLog = "CATGA_AUDIT";

    /// <summary>Projection checkpoint stream name.</summary>
    public const string ProjectionCheckpoint = "CATGA_PROJECTION_CHECKPOINT";

    /// <summary>Subscription store stream name.</summary>
    public const string Subscriptions = "CATGA_SUBSCRIPTIONS";
}
