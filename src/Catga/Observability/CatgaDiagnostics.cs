using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Catga.Observability;

/// <summary>Centralized diagnostics for Catga framework (ActivitySource + Metrics)</summary>
internal static class CatgaDiagnostics
{
    public const string ActivitySourceName = "Catga";
    public const string MeterName = "Catga";

    public static ActivitySource ActivitySource => CatgaActivitySource.Source;
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    // ===== Counters =====
    // Message counters
    public static readonly Counter<long> MessagesPublished = Meter.CreateCounter<long>("catga.messages.published", "messages", "Total messages published");
    public static readonly Counter<long> MessagesFailed = Meter.CreateCounter<long>("catga.messages.failed", "messages", "Total messages failed");
    public static readonly Counter<long> MessagesRetried = Meter.CreateCounter<long>("catga.messages.retried", "messages", "Total messages retried");
    public static readonly Counter<long> NatsDedupDrops = Meter.CreateCounter<long>("catga.nats.dedup.drops", "messages", "Duplicates dropped by NATS transport deduplication");
    public static readonly Counter<long> NatsDedupEvictions = Meter.CreateCounter<long>("catga.nats.dedup.evictions", "items", "Dedup cache evictions in NATS transport");

    // Resilience (generic across components)
    public static readonly Counter<long> ResilienceRetries = Meter.CreateCounter<long>("catga.resilience.retries", "operations", "Total retries executed by resilience policies");
    public static readonly Counter<long> ResilienceTimeouts = Meter.CreateCounter<long>("catga.resilience.timeouts", "operations", "Total timeouts triggered by resilience policies");
    public static readonly Counter<long> ResilienceCircuitOpened = Meter.CreateCounter<long>("catga.resilience.circuit.opened", "events", "Circuit breaker opened events");
    public static readonly Counter<long> ResilienceCircuitHalfOpened = Meter.CreateCounter<long>("catga.resilience.circuit.half_opened", "events", "Circuit breaker half-opened events");
    public static readonly Counter<long> ResilienceCircuitClosed = Meter.CreateCounter<long>("catga.resilience.circuit.closed", "events", "Circuit breaker closed events");
    public static readonly Counter<long> ResilienceBulkheadRejected = Meter.CreateCounter<long>("catga.resilience.bulkhead.rejected", "events", "Bulkhead (concurrency limiter) rejections");

    // Command counters
    public static readonly Counter<long> CommandsExecuted = Meter.CreateCounter<long>("catga.commands.executed", "commands", "Total commands executed");
    public static readonly Counter<long> CommandsSucceeded = Meter.CreateCounter<long>("catga.commands.succeeded", "commands", "Total commands succeeded");
    public static readonly Counter<long> CommandsFailed = Meter.CreateCounter<long>("catga.commands.failed", "commands", "Total commands failed");

    // Query counters
    public static readonly Counter<long> QueriesExecuted = Meter.CreateCounter<long>("catga.queries.executed", "queries", "Total queries executed");

    // Event counters
    public static readonly Counter<long> EventsPublished = Meter.CreateCounter<long>("catga.events.published", "events", "Total events published");
    public static readonly Counter<long> EventsHandled = Meter.CreateCounter<long>("catga.events.handled", "events", "Total events handled");
    public static readonly Counter<long> EventsFailed = Meter.CreateCounter<long>("catga.events.failed", "events", "Total event handling failures");

    // Event store counters
    public static readonly Counter<long> EventStoreAppends = Meter.CreateCounter<long>("catga.eventstore.appends", "operations", "Total event store append operations");
    public static readonly Counter<long> EventStoreReads = Meter.CreateCounter<long>("catga.eventstore.reads", "operations", "Total event store read operations");
    public static readonly Counter<long> EventStoreFailures = Meter.CreateCounter<long>("catga.eventstore.failures", "operations", "Total event store failures");

    // Inbox counters
    public static readonly Counter<long> InboxLocksAcquired = Meter.CreateCounter<long>("catga.inbox.locks_acquired", "operations", "Total inbox locks acquired");
    public static readonly Counter<long> InboxProcessed = Meter.CreateCounter<long>("catga.inbox.processed", "messages", "Total inbox messages marked as processed");
    public static readonly Counter<long> InboxLocksReleased = Meter.CreateCounter<long>("catga.inbox.locks_released", "operations", "Total inbox locks released");

    // Outbox counters
    public static readonly Counter<long> OutboxAdded = Meter.CreateCounter<long>("catga.outbox.added", "messages", "Total messages added to outbox");
    public static readonly Counter<long> OutboxPublished = Meter.CreateCounter<long>("catga.outbox.published", "messages", "Total outbox messages marked as published");
    public static readonly Counter<long> OutboxFailed = Meter.CreateCounter<long>("catga.outbox.failed", "messages", "Total outbox messages marked as failed");

    // Dead letter counters
    public static readonly Counter<long> DeadLetters = Meter.CreateCounter<long>("catga.deadletter.messages", "messages", "Total messages sent to dead letter queue");

    // Idempotency counters
    public static readonly Counter<long> IdempotencyHits = Meter.CreateCounter<long>("catga.idempotency.hits", "operations", "Idempotency checks hit (processed)");
    public static readonly Counter<long> IdempotencyMisses = Meter.CreateCounter<long>("catga.idempotency.misses", "operations", "Idempotency checks miss (not processed)");
    public static readonly Counter<long> IdempotencyMarked = Meter.CreateCounter<long>("catga.idempotency.marked", "operations", "Messages marked as processed");
    public static readonly Counter<long> IdempotencyCacheHits = Meter.CreateCounter<long>("catga.idempotency.cache_hits", "operations", "Cached result hits");
    public static readonly Counter<long> IdempotencyCacheMisses = Meter.CreateCounter<long>("catga.idempotency.cache_misses", "operations", "Cached result misses");

    // Distributed lock counters
    public static readonly Counter<long> LocksAcquired = Meter.CreateCounter<long>("catga.lock.acquired", "operations", "Distributed locks acquired");
    public static readonly Counter<long> LocksReleased = Meter.CreateCounter<long>("catga.lock.released", "operations", "Distributed locks released");
    public static readonly Counter<long> LocksFailed = Meter.CreateCounter<long>("catga.lock.failed", "operations", "Distributed lock acquisition failures");
    public static readonly Counter<long> LocksTimeout = Meter.CreateCounter<long>("catga.lock.timeout", "operations", "Distributed lock acquisition timeouts");

    // Rate limiter counters
    public static readonly Counter<long> RateLimitAcquired = Meter.CreateCounter<long>("catga.ratelimit.acquired", "operations", "Rate limit permits acquired");
    public static readonly Counter<long> RateLimitRejected = Meter.CreateCounter<long>("catga.ratelimit.rejected", "operations", "Rate limit rejections");

    // Leader election counters
    public static readonly Counter<long> LeaderElected = Meter.CreateCounter<long>("catga.leader.elected", "events", "Leader election events");
    public static readonly Counter<long> LeaderLost = Meter.CreateCounter<long>("catga.leader.lost", "events", "Leadership lost events");
    public static readonly Counter<long> LeaderExtended = Meter.CreateCounter<long>("catga.leader.extended", "operations", "Leadership lease extensions");

    // Compensation counters
    public static readonly Counter<long> CompensationPublished = Meter.CreateCounter<long>("catga.compensation.published", "events", "Compensation events published");
    public static readonly Counter<long> CompensationFailed = Meter.CreateCounter<long>("catga.compensation.failed", "events", "Compensation failures");

    // Scheduler counters
    public static readonly Counter<long> ScheduledMessages = Meter.CreateCounter<long>("catga.scheduler.scheduled", "messages", "Messages scheduled");
    public static readonly Counter<long> ScheduledDelivered = Meter.CreateCounter<long>("catga.scheduler.delivered", "messages", "Scheduled messages delivered");
    public static readonly Counter<long> ScheduledCancelled = Meter.CreateCounter<long>("catga.scheduler.cancelled", "messages", "Scheduled messages cancelled");

    // ===== Histograms (for P50, P95, P99) =====
    public static readonly Histogram<double> MessageDuration = Meter.CreateHistogram<double>("catga.message.duration", "ms", "Message processing duration");
    public static readonly Histogram<double> CommandDuration = Meter.CreateHistogram<double>("catga.command.duration", "ms", "Command execution duration");
    public static readonly Histogram<double> QueryDuration = Meter.CreateHistogram<double>("catga.query.duration", "ms", "Query execution duration");
    public static readonly Histogram<double> EventDuration = Meter.CreateHistogram<double>("catga.event.duration", "ms", "Event handling duration");
    public static readonly Histogram<long> MessageSize = Meter.CreateHistogram<long>("catga.message.size", "bytes", "Message payload size");

    // Event store duration
    public static readonly Histogram<double> EventStoreAppendDuration = Meter.CreateHistogram<double>("catga.eventstore.append.duration", "ms", "Event store append duration");
    public static readonly Histogram<double> EventStoreReadDuration = Meter.CreateHistogram<double>("catga.eventstore.read.duration", "ms", "Event store read duration");

    // Distributed lock duration
    public static readonly Histogram<double> LockAcquireDuration = Meter.CreateHistogram<double>("catga.lock.acquire.duration", "ms", "Lock acquisition duration");
    public static readonly Histogram<double> LockHoldDuration = Meter.CreateHistogram<double>("catga.lock.hold.duration", "ms", "Lock hold duration");

    // Rate limiter duration
    public static readonly Histogram<double> RateLimitWaitDuration = Meter.CreateHistogram<double>("catga.ratelimit.wait.duration", "ms", "Rate limit wait duration");

    // Pipeline metrics
    public static readonly Histogram<double> PipelineDuration = Meter.CreateHistogram<double>("catga.pipeline.duration", "ms", "Pipeline execution duration");
    public static readonly Histogram<int> PipelineBehaviorCount = Meter.CreateHistogram<int>("catga.pipeline.behavior_count", "behaviors", "Number of behaviors in pipeline");
    public static readonly Histogram<double> PipelineBehaviorDuration = Meter.CreateHistogram<double>("catga.pipeline.behavior.duration", "ms", "Single pipeline behavior execution duration");

    public static readonly Histogram<int> MediatorBatchSize = Meter.CreateHistogram<int>("catga.mediator.batch.size", "items", "Mediator batch size");
    public static readonly Histogram<double> MediatorBatchFlushDuration = Meter.CreateHistogram<double>("catga.mediator.batch.flush.duration", "ms", "Mediator batch flush duration");
    public static readonly Histogram<int> MediatorBatchQueueLength = Meter.CreateHistogram<int>("catga.mediator.batch.queue_length", "items", "Mediator batch queue length");
    public static readonly Counter<long> MediatorBatchOverflow = Meter.CreateCounter<long>("catga.mediator.batch.overflow", "items", "Mediator batch queue overflow drops");

    // DI metrics
    public static readonly Counter<long> DIRegistrationsCompleted = Meter.CreateCounter<long>("catga.di.registrations.completed", "registrations", "DI registrations completed");
    public static readonly Counter<long> DIRegistrationsFailed = Meter.CreateCounter<long>("catga.di.registrations.failed", "registrations", "DI registrations failed");
    public static readonly Histogram<double> DIRegistrationDuration = Meter.CreateHistogram<double>("catga.di.registration.duration", "ms", "DI registration duration");

    // ===== Gauges (ObservableGauge) =====
    private static long _activeMessages;
    private static long _queuedMessages;
    private static long _activeHandlers;

    public static readonly ObservableGauge<long> ActiveMessages = Meter.CreateObservableGauge("catga.messages.active", () => _activeMessages, "messages", "Active messages being processed");
    public static readonly ObservableGauge<long> QueuedMessages = Meter.CreateObservableGauge("catga.messages.queued", () => _queuedMessages, "messages", "Messages in queue");
    public static readonly ObservableGauge<long> ActiveHandlers = Meter.CreateObservableGauge("catga.handlers.active", () => _activeHandlers, "handlers", "Active event handlers running");

    // Helper methods for gauge updates
    public static void IncrementActiveMessages() => Interlocked.Increment(ref _activeMessages);
    public static void DecrementActiveMessages() => Interlocked.Decrement(ref _activeMessages);
    public static void IncrementQueuedMessages() => Interlocked.Increment(ref _queuedMessages);
    public static void DecrementQueuedMessages() => Interlocked.Decrement(ref _queuedMessages);
    public static void IncrementActiveHandlers() => Interlocked.Increment(ref _activeHandlers);
    public static void DecrementActiveHandlers() => Interlocked.Decrement(ref _activeHandlers);
}

