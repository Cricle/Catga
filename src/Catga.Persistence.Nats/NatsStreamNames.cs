namespace Catga.Persistence.Nats;

/// <summary>Centralized NATS JetStream stream names for all stores.</summary>
public static class NatsStreamNames
{
    public const string DefaultPrefix = "CATGA", Idempotency = "CATGA_IDEMPOTENCY", DeadLetterQueue = "CATGA_DLQ",
        Inbox = "CATGA_INBOX", Outbox = "CATGA_OUTBOX", Events = "CATGA_EVENTS", Snapshots = "CATGA_SNAPSHOTS",
        ProjectionCheckpoint = "CATGA_PROJECTION_CHECKPOINT", Subscriptions = "CATGA_SUBSCRIPTIONS";
}
