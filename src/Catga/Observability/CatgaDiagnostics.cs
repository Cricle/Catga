using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Catga.Observability;

/// <summary>Centralized diagnostics for Catga framework (ActivitySource + Metrics)</summary>
public static class CatgaDiagnostics
{
    public const string ActivitySourceName = "Catga";
    public const string MeterName = "Catga";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    // ===== Counters =====
    // Message counters
    public static readonly Counter<long> MessagesPublished = Meter.CreateCounter<long>("catga.messages.published", "messages", "Total messages published");
    public static readonly Counter<long> MessagesFailed = Meter.CreateCounter<long>("catga.messages.failed", "messages", "Total messages failed");
    public static readonly Counter<long> MessagesRetried = Meter.CreateCounter<long>("catga.messages.retried", "messages", "Total messages retried");
    
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

    // ===== Histograms (for P50, P95, P99) =====
    public static readonly Histogram<double> MessageDuration = Meter.CreateHistogram<double>("catga.message.duration", "ms", "Message processing duration");
    public static readonly Histogram<double> CommandDuration = Meter.CreateHistogram<double>("catga.command.duration", "ms", "Command execution duration");
    public static readonly Histogram<double> QueryDuration = Meter.CreateHistogram<double>("catga.query.duration", "ms", "Query execution duration");
    public static readonly Histogram<double> EventDuration = Meter.CreateHistogram<double>("catga.event.duration", "ms", "Event handling duration");
    public static readonly Histogram<long> MessageSize = Meter.CreateHistogram<long>("catga.message.size", "bytes", "Message payload size");
    
    // Pipeline metrics
    public static readonly Histogram<double> PipelineDuration = Meter.CreateHistogram<double>("catga.pipeline.duration", "ms", "Pipeline execution duration");
    public static readonly Histogram<int> PipelineBehaviorCount = Meter.CreateHistogram<int>("catga.pipeline.behavior_count", "behaviors", "Number of behaviors in pipeline");

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

