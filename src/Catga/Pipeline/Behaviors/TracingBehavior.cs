using System.Diagnostics;
using Catga.Messages;
using Catga.Observability;
using Catga.Results;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Distributed tracing and metrics collection behavior (fully OpenTelemetry compatible)
/// </summary>
public class TracingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly ActivitySource ActivitySource = new("Catga", "1.0.0");

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var requestType = typeof(TRequest).Name;
        var startTime = Diagnostics.GetTimestamp();

        // Create distributed tracing span
        using var activity = ActivitySource.StartActivity(
            $"Catga.Request.{requestType}",
            ActivityKind.Internal);

        // Set standard OpenTelemetry tags
        activity?.SetTag("messaging.system", "catga");
        activity?.SetTag("messaging.operation", "process");
        activity?.SetTag("messaging.message_id", request.MessageId);
        activity?.SetTag("messaging.correlation_id", request.CorrelationId);

        // Set Catga-specific tags
        activity?.SetTag("catga.message_type", requestType);
        activity?.SetTag("catga.request_type", typeof(TRequest).FullName);
        activity?.SetTag("catga.response_type", typeof(TResponse).FullName);
        activity?.SetTag("catga.timestamp", request.CreatedAt);

        // Record metric: request start
        var metricTags = new Dictionary<string, object?>
        {
            ["message.type"] = requestType
        };
        // TODO: Integrate with CatgaMetrics instance
        // CatgaMetrics.RecordRequestStart(requestType, metricTags);

        try
        {
            var result = await next();
            var duration = Diagnostics.GetElapsedTime(startTime).TotalMilliseconds;

            // Update tracing status
            activity?.SetTag("catga.success", result.IsSuccess);
            activity?.SetTag("catga.duration_ms", duration);

            if (result.IsSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);

                // Record metric: success
                // TODO: Integrate with CatgaMetrics instance
                // CatgaMetrics.RecordRequestSuccess(requestType, duration, metricTags);
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

                    // Record exception event
                    activity?.AddEvent(new ActivityEvent("exception",
                        tags: new ActivityTagsCollection
                        {
                            ["exception.type"] = result.Exception.GetType().FullName,
                            ["exception.message"] = result.Exception.Message
                        }));
                }

                // Record metric: failure
                // TODO: Integrate with CatgaMetrics instance
                // var errorType = result.Exception?.GetType().Name ?? "UnknownError";
                // CatgaMetrics.RecordRequestFailure(requestType, errorType, duration, metricTags);
            }

            return result;
        }
        catch (Exception ex)
        {
            var duration = Diagnostics.GetElapsedTime(startTime).TotalMilliseconds;

            // Update tracing status
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().FullName);
            activity?.SetTag("exception.message", ex.Message);
            activity?.SetTag("exception.stacktrace", ex.StackTrace);
            activity?.SetTag("catga.duration_ms", duration);

            // Record exception event
            activity?.AddEvent(new ActivityEvent("exception",
                tags: new ActivityTagsCollection
                {
                    ["exception.type"] = ex.GetType().FullName,
                    ["exception.message"] = ex.Message,
                    ["exception.escaped"] = true
                }));

            // Record metric: failure
            // TODO: Integrate with CatgaMetrics instance
            // CatgaMetrics.RecordRequestFailure(requestType, ex.GetType().Name, duration, metricTags);

            throw;
        }
    }
}

/// <summary>
/// High-performance timestamp utilities (avoid DateTime.UtcNow allocations)
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

