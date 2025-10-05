using System.Diagnostics;
using Catga.Messages;
using Catga.Observability;
using Catga.Results;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// 分布式追踪和指标收集行为（OpenTelemetry 完全兼容）
/// </summary>
public class TracingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly ActivitySource ActivitySource = new("Catga", "1.0.0");

    public async Task<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        Func<Task<CatgaResult<TResponse>>> next,
        CancellationToken cancellationToken = default)
    {
        var requestType = typeof(TRequest).Name;
        var startTime = Diagnostics.GetTimestamp();

        // 创建分布式追踪 Span
        using var activity = ActivitySource.StartActivity(
            $"Catga.Request.{requestType}",
            ActivityKind.Internal);

        // 设置标准 OpenTelemetry 标签
        activity?.SetTag("messaging.system", "catga");
        activity?.SetTag("messaging.operation", "process");
        activity?.SetTag("messaging.message_id", request.MessageId);
        activity?.SetTag("messaging.correlation_id", request.CorrelationId);

        // 设置 Catga 特定标签
        activity?.SetTag("catga.message_type", requestType);
        activity?.SetTag("catga.request_type", typeof(TRequest).FullName);
        activity?.SetTag("catga.response_type", typeof(TResponse).FullName);
        activity?.SetTag("catga.timestamp", request.CreatedAt);

        // 记录指标：请求开始
        var metricTags = new Dictionary<string, object?>
        {
            ["message.type"] = requestType
        };
        CatgaMetrics.RecordRequestStart(requestType, metricTags);

        try
        {
            var result = await next();
            var duration = Diagnostics.GetElapsedTime(startTime).TotalMilliseconds;

            // 更新追踪状态
            activity?.SetTag("catga.success", result.IsSuccess);
            activity?.SetTag("catga.duration_ms", duration);

            if (result.IsSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);

                // 记录指标：成功
                CatgaMetrics.RecordRequestSuccess(requestType, duration, metricTags);
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.Error);
                activity?.SetTag("catga.error_message", result.Error);

                if (result.Exception != null)
                {
                    activity?.SetTag("exception.type", result.Exception.GetType().Name);
                    activity?.SetTag("exception.message", result.Exception.Message);
                    activity?.SetTag("exception.stacktrace", result.Exception.StackTrace);

                    // 记录异常事件
                    activity?.AddEvent(new ActivityEvent("exception",
                        tags: new ActivityTagsCollection
                        {
                            ["exception.type"] = result.Exception.GetType().FullName,
                            ["exception.message"] = result.Exception.Message
                        }));
                }

                // 记录指标：失败
                var errorType = result.Exception?.GetType().Name ?? "UnknownError";
                CatgaMetrics.RecordRequestFailure(requestType, errorType, duration, metricTags);
            }

            return result;
        }
        catch (Exception ex)
        {
            var duration = Diagnostics.GetElapsedTime(startTime).TotalMilliseconds;

            // 更新追踪状态
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().FullName);
            activity?.SetTag("exception.message", ex.Message);
            activity?.SetTag("exception.stacktrace", ex.StackTrace);
            activity?.SetTag("catga.duration_ms", duration);

            // 记录异常事件
            activity?.AddEvent(new ActivityEvent("exception",
                tags: new ActivityTagsCollection
                {
                    ["exception.type"] = ex.GetType().FullName,
                    ["exception.message"] = ex.Message,
                    ["exception.escaped"] = true
                }));

            // 记录指标：失败
            CatgaMetrics.RecordRequestFailure(requestType, ex.GetType().Name, duration, metricTags);

            throw;
        }
    }
}

/// <summary>
/// 高性能时间戳工具（避免 DateTime.UtcNow 的分配）
/// </summary>
internal static class Diagnostics
{
    private static readonly double TimestampToTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;

    public static long GetTimestamp() => Stopwatch.GetTimestamp();

    public static TimeSpan GetElapsedTime(long startingTimestamp)
    {
        var timestampDelta = Stopwatch.GetTimestamp() - startingTimestamp;
        var ticks = (long)(TimestampToTicks * timestampDelta);
        return new TimeSpan(ticks);
    }
}

