using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Catga.Observability;

/// <summary>Centralized metrics for Catga framework (single Meter for all components)</summary>
internal static class CatgaDiagnostics
{
    public const string MeterName = "Catga";
    public static readonly Meter Meter = new(MeterName, "1.0.0");
    public static ActivitySource ActivitySource => CatgaActivitySource.Source;

    // ========== Counters ==========

    // Messages
    public static readonly Counter<long> MessagesPublished = Meter.CreateCounter<long>("catga.messages.published");
    public static readonly Counter<long> MessagesFailed = Meter.CreateCounter<long>("catga.messages.failed");

    // Commands
    public static readonly Counter<long> CommandsExecuted = Meter.CreateCounter<long>("catga.commands.executed");

    // Events
    public static readonly Counter<long> EventsPublished = Meter.CreateCounter<long>("catga.events.published");
    public static readonly Counter<long> EventsFailed = Meter.CreateCounter<long>("catga.events.failed");

    // EventStore
    public static readonly Counter<long> EventStoreAppends = Meter.CreateCounter<long>("catga.eventstore.appends");
    public static readonly Counter<long> EventStoreReads = Meter.CreateCounter<long>("catga.eventstore.reads");
    public static readonly Counter<long> EventStoreFailures = Meter.CreateCounter<long>("catga.eventstore.failures");

    // Inbox/Outbox
    public static readonly Counter<long> InboxProcessed = Meter.CreateCounter<long>("catga.inbox.processed");
    public static readonly Counter<long> InboxLocksAcquired = Meter.CreateCounter<long>("catga.inbox.locks.acquired");
    public static readonly Counter<long> InboxLocksReleased = Meter.CreateCounter<long>("catga.inbox.locks.released");
    public static readonly Counter<long> OutboxAdded = Meter.CreateCounter<long>("catga.outbox.added");
    public static readonly Counter<long> OutboxPublished = Meter.CreateCounter<long>("catga.outbox.published");
    public static readonly Counter<long> OutboxFailed = Meter.CreateCounter<long>("catga.outbox.failed");

    // Dead Letter
    public static readonly Counter<long> DeadLetters = Meter.CreateCounter<long>("catga.deadletter.messages");

    // Idempotency
    public static readonly Counter<long> IdempotencyHits = Meter.CreateCounter<long>("catga.idempotency.hits");
    public static readonly Counter<long> IdempotencyMisses = Meter.CreateCounter<long>("catga.idempotency.misses");

    // Locks
    public static readonly Counter<long> LocksAcquired = Meter.CreateCounter<long>("catga.lock.acquired");
    public static readonly Counter<long> LocksFailed = Meter.CreateCounter<long>("catga.lock.failed");

    // Resilience
    public static readonly Counter<long> ResilienceRetries = Meter.CreateCounter<long>("catga.resilience.retries");
    public static readonly Counter<long> ResilienceCircuitOpened = Meter.CreateCounter<long>("catga.resilience.circuit.opened");

    // Batching
    public static readonly Counter<long> MediatorBatchOverflow = Meter.CreateCounter<long>("catga.mediator.batch.overflow");

    // Flow
    public static readonly Counter<long> FlowsStarted = Meter.CreateCounter<long>("catga.flow.started");
    public static readonly Counter<long> FlowsCompleted = Meter.CreateCounter<long>("catga.flow.completed");
    public static readonly Counter<long> FlowsFailed = Meter.CreateCounter<long>("catga.flow.failed");
    public static readonly Counter<long> StepsExecuted = Meter.CreateCounter<long>("catga.flow.step.executed");
    public static readonly Counter<long> StepsSucceeded = Meter.CreateCounter<long>("catga.flow.step.succeeded");
    public static readonly Counter<long> StepsFailed = Meter.CreateCounter<long>("catga.flow.step.failed");

    // ========== Histograms ==========

    public static readonly Histogram<double> CommandDuration = Meter.CreateHistogram<double>("catga.command.duration", "ms");
    public static readonly Histogram<double> EventDuration = Meter.CreateHistogram<double>("catga.event.duration", "ms");
    public static readonly Histogram<double> PipelineDuration = Meter.CreateHistogram<double>("catga.pipeline.duration", "ms");
    public static readonly Histogram<int> PipelineBehaviorCount = Meter.CreateHistogram<int>("catga.pipeline.behavior_count");
    public static readonly Histogram<double> EventStoreAppendDuration = Meter.CreateHistogram<double>("catga.eventstore.append.duration", "ms");
    public static readonly Histogram<double> EventStoreReadDuration = Meter.CreateHistogram<double>("catga.eventstore.read.duration", "ms");
    public static readonly Histogram<double> LockAcquireDuration = Meter.CreateHistogram<double>("catga.lock.acquire.duration", "ms");
    public static readonly Histogram<int> MediatorBatchSize = Meter.CreateHistogram<int>("catga.mediator.batch.size");
    public static readonly Histogram<double> MediatorBatchFlushDuration = Meter.CreateHistogram<double>("catga.mediator.batch.flush.duration", "ms");
    public static readonly Histogram<double> FlowDuration = Meter.CreateHistogram<double>("catga.flow.duration", "ms");
    public static readonly Histogram<double> StepDuration = Meter.CreateHistogram<double>("catga.flow.step.duration", "ms");

    // ========== Gauges ==========

    private static long _activeMessages;
    private static long _activeFlows;

    public static readonly ObservableGauge<long> ActiveMessages = Meter.CreateObservableGauge("catga.messages.active", () => _activeMessages);
    public static readonly ObservableGauge<long> ActiveFlows = Meter.CreateObservableGauge("catga.flow.active", () => _activeFlows);

    public static void IncrementActiveMessages() => Interlocked.Increment(ref _activeMessages);
    public static void DecrementActiveMessages() => Interlocked.Decrement(ref _activeMessages);
    public static void IncrementActiveFlows() => Interlocked.Increment(ref _activeFlows);
    public static void DecrementActiveFlows() => Interlocked.Decrement(ref _activeFlows);
}

