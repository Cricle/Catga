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

    // Counters
    public static readonly Counter<long> MessagesPublished = Meter.CreateCounter<long>("catga.messages.published", "messages", "Total messages published");
    public static readonly Counter<long> MessagesFailed = Meter.CreateCounter<long>("catga.messages.failed", "messages", "Total messages failed");
    public static readonly Counter<long> CommandsExecuted = Meter.CreateCounter<long>("catga.commands.executed", "commands", "Total commands executed");
    public static readonly Counter<long> QueriesExecuted = Meter.CreateCounter<long>("catga.queries.executed", "queries", "Total queries executed");
    public static readonly Counter<long> EventsPublished = Meter.CreateCounter<long>("catga.events.published", "events", "Total events published");

    // Histograms
    public static readonly Histogram<double> MessageDuration = Meter.CreateHistogram<double>("catga.message.duration", "ms", "Message processing duration");
    public static readonly Histogram<double> CommandDuration = Meter.CreateHistogram<double>("catga.command.duration", "ms", "Command execution duration");
    public static readonly Histogram<double> QueryDuration = Meter.CreateHistogram<double>("catga.query.duration", "ms", "Query execution duration");
    public static readonly Histogram<long> MessageSize = Meter.CreateHistogram<long>("catga.message.size", "bytes", "Message payload size");

    // Gauges (ObservableGauge)
    private static long _activeMessages;
    private static long _queuedMessages;

    public static readonly ObservableGauge<long> ActiveMessages = Meter.CreateObservableGauge("catga.messages.active", () => _activeMessages, "messages", "Active messages being processed");
    public static readonly ObservableGauge<long> QueuedMessages = Meter.CreateObservableGauge("catga.messages.queued", () => _queuedMessages, "messages", "Messages in queue");

    public static void IncrementActiveMessages() => Interlocked.Increment(ref _activeMessages);
    public static void DecrementActiveMessages() => Interlocked.Decrement(ref _activeMessages);
    public static void IncrementQueuedMessages() => Interlocked.Increment(ref _queuedMessages);
    public static void DecrementQueuedMessages() => Interlocked.Decrement(ref _queuedMessages);
}

