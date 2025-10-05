using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Catga.Observability;

/// <summary>
/// Catga 框架指标收集器（基于 OpenTelemetry Metrics）
/// </summary>
public sealed class CatgaMetrics : IDisposable
{
    private static readonly Meter Meter = new("Catga", "1.0.0");

    // 计数器 (Counters)
    private static readonly Counter<long> RequestsTotal = Meter.CreateCounter<long>(
        "catga.requests.total",
        description: "请求总数");

    private static readonly Counter<long> RequestsSucceeded = Meter.CreateCounter<long>(
        "catga.requests.succeeded",
        description: "成功请求数");

    private static readonly Counter<long> RequestsFailed = Meter.CreateCounter<long>(
        "catga.requests.failed",
        description: "失败请求数");

    private static readonly Counter<long> EventsPublished = Meter.CreateCounter<long>(
        "catga.events.published",
        description: "发布的事件数");

    private static readonly Counter<long> RetryAttempts = Meter.CreateCounter<long>(
        "catga.retry.attempts",
        description: "重试尝试次数");

    private static readonly Counter<long> CircuitBreakerOpened = Meter.CreateCounter<long>(
        "catga.circuit_breaker.opened",
        description: "熔断器打开次数");

    private static readonly Counter<long> IdempotentRequestsSkipped = Meter.CreateCounter<long>(
        "catga.idempotency.skipped",
        description: "因幂等性跳过的请求数");

    // 直方图 (Histograms)
    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "catga.request.duration",
        unit: "ms",
        description: "请求处理时长");

    private static readonly Histogram<double> EventHandlingDuration = Meter.CreateHistogram<double>(
        "catga.event.handling_duration",
        unit: "ms",
        description: "事件处理时长");

    private static readonly Histogram<double> SagaDuration = Meter.CreateHistogram<double>(
        "catga.saga.duration",
        unit: "ms",
        description: "Saga 执行时长");

    // 仪表盘 (Gauges) - 使用 ObservableGauge
    private static long _activeRequests;
    private static long _activeSagas;
    private static long _queuedMessages;

    private static readonly ObservableGauge<long> ActiveRequests = Meter.CreateObservableGauge(
        "catga.requests.active",
        () => Interlocked.Read(ref _activeRequests),
        description: "当前活跃请求数");

    private static readonly ObservableGauge<long> ActiveSagas = Meter.CreateObservableGauge(
        "catga.sagas.active",
        () => Interlocked.Read(ref _activeSagas),
        description: "当前活跃 Saga 数");

    private static readonly ObservableGauge<long> QueuedMessages = Meter.CreateObservableGauge(
        "catga.messages.queued",
        () => Interlocked.Read(ref _queuedMessages),
        description: "队列中的消息数");

    /// <summary>
    /// 记录请求开始
    /// </summary>
    public static void RecordRequestStart(string requestType, IDictionary<string, object?>? tags = null)
    {
        Interlocked.Increment(ref _activeRequests);
        RequestsTotal.Add(1, CreateTagList(requestType, tags));
    }

    /// <summary>
    /// 记录请求成功
    /// </summary>
    public static void RecordRequestSuccess(string requestType, double durationMs, IDictionary<string, object?>? tags = null)
    {
        Interlocked.Decrement(ref _activeRequests);
        RequestsSucceeded.Add(1, CreateTagList(requestType, tags));
        RequestDuration.Record(durationMs, CreateTagList(requestType, tags));
    }

    /// <summary>
    /// 记录请求失败
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
    /// 记录事件发布
    /// </summary>
    public static void RecordEventPublished(string eventType, IDictionary<string, object?>? tags = null)
    {
        EventsPublished.Add(1, CreateTagList(eventType, tags));
    }

    /// <summary>
    /// 记录事件处理
    /// </summary>
    public static void RecordEventHandling(string eventType, double durationMs, bool success, IDictionary<string, object?>? tags = null)
    {
        var tagList = CreateTagList(eventType, tags);
        tagList.Add("success", success);
        EventHandlingDuration.Record(durationMs, tagList);
    }

    /// <summary>
    /// 记录重试尝试
    /// </summary>
    public static void RecordRetryAttempt(string requestType, int attemptNumber, IDictionary<string, object?>? tags = null)
    {
        var tagList = CreateTagList(requestType, tags);
        tagList.Add("attempt", attemptNumber);
        RetryAttempts.Add(1, tagList);
    }

    /// <summary>
    /// 记录熔断器打开
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
    /// 记录幂等性跳过
    /// </summary>
    public static void RecordIdempotentSkipped(string requestType, IDictionary<string, object?>? tags = null)
    {
        IdempotentRequestsSkipped.Add(1, CreateTagList(requestType, tags));
    }

    /// <summary>
    /// 记录 Saga 开始
    /// </summary>
    public static void RecordSagaStart(string sagaType)
    {
        Interlocked.Increment(ref _activeSagas);
    }

    /// <summary>
    /// 记录 Saga 完成
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
    /// 更新队列消息数
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

