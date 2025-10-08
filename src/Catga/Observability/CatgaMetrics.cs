using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Catga.Observability;

/// <summary>
/// Catga framework metrics collector (based on OpenTelemetry Metrics)
/// </summary>
public sealed class CatgaMetrics : IDisposable
{
    private static readonly Meter Meter = new("Catga", "1.0.0");

    // Counters
    private static readonly Counter<long> RequestsTotal = Meter.CreateCounter<long>(
        "catga.requests.total",
        description: "Total number of requests");

    private static readonly Counter<long> RequestsSucceeded = Meter.CreateCounter<long>(
        "catga.requests.succeeded",
        description: "Number of successful requests");

    private static readonly Counter<long> RequestsFailed = Meter.CreateCounter<long>(
        "catga.requests.failed",
        description: "Number of failed requests");

    private static readonly Counter<long> EventsPublished = Meter.CreateCounter<long>(
        "catga.events.published",
        description: "Number of published events");

    private static readonly Counter<long> RetryAttempts = Meter.CreateCounter<long>(
        "catga.retry.attempts",
        description: "Number of retry attempts");

    private static readonly Counter<long> CircuitBreakerOpened = Meter.CreateCounter<long>(
        "catga.circuit_breaker.opened",
        description: "Number of times circuit breaker opened");

    private static readonly Counter<long> IdempotentRequestsSkipped = Meter.CreateCounter<long>(
        "catga.idempotency.skipped",
        description: "Number of requests skipped due to idempotency");

    // Histograms
    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "catga.request.duration",
        unit: "ms",
        description: "Request processing duration");

    private static readonly Histogram<double> EventHandlingDuration = Meter.CreateHistogram<double>(
        "catga.event.handling_duration",
        unit: "ms",
        description: "Event handling duration");

    private static readonly Histogram<double> SagaDuration = Meter.CreateHistogram<double>(
        "catga.saga.duration",
        unit: "ms",
        description: "Saga execution duration");

    // Gauges - using ObservableGauge
    private static long _activeRequests;
    private static long _activeSagas;
    private static long _queuedMessages;

    private static readonly ObservableGauge<long> ActiveRequests = Meter.CreateObservableGauge(
        "catga.requests.active",
        () => Interlocked.Read(ref _activeRequests),
        description: "Number of active requests");

    private static readonly ObservableGauge<long> ActiveSagas = Meter.CreateObservableGauge(
        "catga.sagas.active",
        () => Interlocked.Read(ref _activeSagas),
        description: "Number of active sagas");

    private static readonly ObservableGauge<long> QueuedMessages = Meter.CreateObservableGauge(
        "catga.messages.queued",
        () => Interlocked.Read(ref _queuedMessages),
        description: "Number of queued messages");

    /// <summary>
    /// Record request start
    /// </summary>
    public static void RecordRequestStart(string requestType, IDictionary<string, object?>? tags = null)
    {
        Interlocked.Increment(ref _activeRequests);
        RequestsTotal.Add(1, CreateTagList(requestType, tags));
    }

    /// <summary>
    /// Record request success
    /// </summary>
    public static void RecordRequestSuccess(string requestType, double durationMs, IDictionary<string, object?>? tags = null)
    {
        Interlocked.Decrement(ref _activeRequests);
        RequestsSucceeded.Add(1, CreateTagList(requestType, tags));
        RequestDuration.Record(durationMs, CreateTagList(requestType, tags));
    }

    /// <summary>
    /// Record request failure
    /// </summary>
    public static void RecordRequestFailure(string requestType, string errorType, double durationMs, IDictionary<string, object?>? tags = null)
    {
        Interlocked.Decrement(ref _activeRequests);
        var tagList = CreateTagList(requestType, tags);
        tagList.Add("error.type", errorType);

        RequestsFailed.Add(1, tagList);
        RequestDuration.Record(durationMs, tagList);
    }

    /// <summary>
    /// Record event published
    /// </summary>
    public static void RecordEventPublished(string eventType, IDictionary<string, object?>? tags = null)
    {
        EventsPublished.Add(1, CreateTagList(eventType, tags));
    }

    /// <summary>
    /// Record event handling
    /// </summary>
    public static void RecordEventHandling(string eventType, double durationMs, bool success, IDictionary<string, object?>? tags = null)
    {
        var tagList = CreateTagList(eventType, tags);
        tagList.Add("success", success);
        EventHandlingDuration.Record(durationMs, tagList);
    }

    /// <summary>
    /// Record retry attempt
    /// </summary>
    public static void RecordRetryAttempt(string requestType, int attemptNumber, IDictionary<string, object?>? tags = null)
    {
        var tagList = CreateTagList(requestType, tags);
        tagList.Add("attempt", attemptNumber);
        RetryAttempts.Add(1, tagList);
    }

    /// <summary>
    /// Record circuit breaker opened
    /// </summary>
    public static void RecordCircuitBreakerOpened(string circuitName, IDictionary<string, object?>? tags = null)
    {
        var tagList = new TagList { { "circuit.name", circuitName } };
        if (tags != null)
        {
            foreach (var tag in tags)
                tagList.Add(tag.Key, tag.Value);
        }
        CircuitBreakerOpened.Add(1, tagList);
    }

    /// <summary>
    /// Record idempotent request skipped
    /// </summary>
    public static void RecordIdempotentSkipped(string requestType, IDictionary<string, object?>? tags = null)
    {
        IdempotentRequestsSkipped.Add(1, CreateTagList(requestType, tags));
    }

    /// <summary>
    /// Record saga start
    /// </summary>
    public static void RecordSagaStart(string sagaType)
    {
        Interlocked.Increment(ref _activeSagas);
    }

    /// <summary>
    /// Record saga complete
    /// </summary>
    public static void RecordSagaComplete(string sagaType, double durationMs, bool success, bool compensated = false)
    {
        Interlocked.Decrement(ref _activeSagas);
        var tags = new TagList
        {
            { "saga.type", sagaType },
            { "success", success },
            { "compensated", compensated }
        };
        SagaDuration.Record(durationMs, tags);
    }

    /// <summary>
    /// Update queued messages count
    /// </summary>
    public static void UpdateQueuedMessages(long count)
    {
        Interlocked.Exchange(ref _queuedMessages, count);
    }

    private static TagList CreateTagList(string operationType, IDictionary<string, object?>? additionalTags = null)
    {
        var tags = new TagList { { "operation.type", operationType } };

        if (additionalTags != null)
        {
            foreach (var tag in additionalTags)
                tags.Add(tag.Key, tag.Value);
        }

        return tags;
    }

    public void Dispose()
    {
        Meter.Dispose();
    }
}

