using System.Diagnostics.Metrics;

namespace Catga.Debugger.Observability;

/// <summary>
/// Catga framework metrics using OpenTelemetry Meter API
/// All metrics are exported to Prometheus automatically
/// </summary>
public sealed class CatgaMetrics
{
    private static readonly Meter _meter = new("Catga.Framework", "1.0.0");

    // Counters
    private static readonly Counter<long> _commandsExecuted = _meter.CreateCounter<long>(
        "catga.commands.executed",
        description: "Total number of commands executed");

    private static readonly Counter<long> _eventsPublished = _meter.CreateCounter<long>(
        "catga.events.published",
        description: "Total number of events published");

    private static readonly Counter<long> _eventHandlersExecuted = _meter.CreateCounter<long>(
        "catga.event_handlers.executed",
        description: "Total number of event handlers executed");

    // Histograms
    private static readonly Histogram<double> _commandDuration = _meter.CreateHistogram<double>(
        "catga.command.duration",
        unit: "s",
        description: "Command execution duration in seconds");

    private static readonly Histogram<double> _eventHandlerDuration = _meter.CreateHistogram<double>(
        "catga.event_handler.duration",
        unit: "s",
        description: "Event handler execution duration in seconds");

    // Gauges (ObservableGauge)
    private static long _activeCommands;
    private static long _activeFlows;
    private static long _eventStoreSize;
    private static long _replaySessions;
    private static long _circuitBreakerOpen;

    static CatgaMetrics()
    {
        _meter.CreateObservableGauge("catga.commands.active",
            () => _activeCommands,
            description: "Number of currently executing commands");

        _meter.CreateObservableGauge("catga.flows.active",
            () => _activeFlows,
            description: "Number of active message flows");

        _meter.CreateObservableGauge("catga.event_store.size_bytes",
            () => _eventStoreSize,
            unit: "By",
            description: "Event store memory usage in bytes");

        _meter.CreateObservableGauge("catga.replay.sessions_active",
            () => _replaySessions,
            description: "Number of active replay sessions");

        _meter.CreateObservableGauge("catga.circuit_breaker.open",
            () => _circuitBreakerOpen,
            description: "Circuit breaker status (0=closed, 1=open)");
    }

    // Command metrics
    public static void RecordCommandExecuted(string requestType, bool success)
    {
        _commandsExecuted.Add(1,
            new KeyValuePair<string, object?>("request_type", requestType),
            new KeyValuePair<string, object?>("success", success.ToString().ToLower()));
    }

    public static void RecordCommandDuration(string requestType, double durationSeconds)
    {
        _commandDuration.Record(durationSeconds,
            new KeyValuePair<string, object?>("request_type", requestType));
    }

    public static void IncrementActiveCommands() => Interlocked.Increment(ref _activeCommands);
    public static void DecrementActiveCommands() => Interlocked.Decrement(ref _activeCommands);

    // Event metrics
    public static void RecordEventPublished(string eventType)
    {
        _eventsPublished.Add(1,
            new KeyValuePair<string, object?>("event_type", eventType));
    }

    public static void RecordEventHandlerExecuted(string handlerType, bool success)
    {
        _eventHandlersExecuted.Add(1,
            new KeyValuePair<string, object?>("handler_type", handlerType),
            new KeyValuePair<string, object?>("success", success.ToString().ToLower()));
    }

    public static void RecordEventHandlerDuration(string handlerType, double durationSeconds)
    {
        _eventHandlerDuration.Record(durationSeconds,
            new KeyValuePair<string, object?>("handler_type", handlerType));
    }

    // System metrics
    public static void SetActiveFlows(long count) => Interlocked.Exchange(ref _activeFlows, count);
    public static void SetEventStoreSize(long bytes) => Interlocked.Exchange(ref _eventStoreSize, bytes);
    public static void SetReplaySessions(long count) => Interlocked.Exchange(ref _replaySessions, count);
    public static void SetCircuitBreakerOpen(bool isOpen) =>
        Interlocked.Exchange(ref _circuitBreakerOpen, isOpen ? 1 : 0);
}

