namespace Catga.Persistence.Redis;

/// <summary>
/// Centralized Redis key prefixes for all stores (DRY principle).
/// </summary>
public static class RedisKeyPrefixes
{
    /// <summary>Flow store key prefix.</summary>
    public const string Flow = "flow:";

    /// <summary>DSL flow store key prefix.</summary>
    public const string DslFlow = "dslflow:";

    /// <summary>Event store key prefix.</summary>
    public const string Events = "events:";

    /// <summary>Snapshot store key prefix.</summary>
    public const string Snapshot = "snapshot:";

    /// <summary>Enhanced snapshot store key prefix.</summary>
    public const string SnapshotEnhanced = "snapshot:enhanced:";

    /// <summary>Audit log store key prefix.</summary>
    public const string Audit = "audit:";

    /// <summary>Projection checkpoint store key prefix.</summary>
    public const string ProjectionCheckpoint = "projection:checkpoint:";

    /// <summary>Subscription store key prefix.</summary>
    public const string Subscription = "subscription:";

    /// <summary>Dead letter queue key prefix.</summary>
    public const string DeadLetterQueue = "dlq:";
}
