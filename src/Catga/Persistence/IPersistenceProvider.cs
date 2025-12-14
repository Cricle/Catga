using Catga.DeadLetter;
using Catga.EventSourcing;
using Catga.Flow;
using Catga.Flow.Dsl;
using Catga.Idempotency;
using Catga.Inbox;
using Catga.Locking;
using Catga.Outbox;

namespace Catga.Persistence;

/// <summary>
/// Provider interface for persistence implementations.
/// Implement this to create a new persistence backend (e.g., Marten, MongoDB).
/// </summary>
public interface IPersistenceProvider
{
    /// <summary>Provider name (e.g., "Redis", "NATS", "Marten").</summary>
    string Name { get; }

    /// <summary>Create DSL flow store.</summary>
    IDslFlowStore? CreateDslFlowStore();

    /// <summary>Create outbox store.</summary>
    IOutboxStore? CreateOutboxStore();

    /// <summary>Create inbox store.</summary>
    IInboxStore? CreateInboxStore();

    /// <summary>Create event store.</summary>
    IEventStore? CreateEventStore();

    /// <summary>Create idempotency store.</summary>
    IIdempotencyStore? CreateIdempotencyStore();

    /// <summary>Create dead letter queue.</summary>
    IDeadLetterQueue? CreateDeadLetterQueue();

    /// <summary>Create snapshot store.</summary>
    ISnapshotStore? CreateSnapshotStore();

    /// <summary>Create distributed lock provider.</summary>
    IDistributedLockProvider? CreateDistributedLockProvider();

    /// <summary>Create flow store (saga).</summary>
    IFlowStore? CreateFlowStore();

    /// <summary>Create projection checkpoint store.</summary>
    IProjectionCheckpointStore? CreateProjectionCheckpointStore();
}
