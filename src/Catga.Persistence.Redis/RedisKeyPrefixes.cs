namespace Catga.Persistence.Redis;

/// <summary>Centralized Redis key prefixes for all stores.</summary>
public static class RedisKeyPrefixes
{
    public const string Flow = "flow:", DslFlow = "dslflow:", Events = "events:", Snapshot = "snapshot:",
        SnapshotEnhanced = "snapshot:enhanced:", ProjectionCheckpoint = "projection:checkpoint:",
        Subscription = "subscription:", DeadLetterQueue = "dlq:";
}
