using System.Diagnostics;
using Catga.Messages;
using Catga.Results;

namespace Catga.Pipeline.Behaviors;

/// <summary>Distributed tracing behavior (OpenTelemetry compatible)</summary>
public class TracingBehavior<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TRequest, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    private static readonly ActivitySource ActivitySource = new("Catga", "1.0.0");

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        var requestType = typeof(TRequest).Name;
        var startTime = Diagnostics.GetTimestamp();
        using var activity = ActivitySource.StartActivity($"Catga.Request.{requestType}", ActivityKind.Internal);

        activity?.SetTag("messaging.system", "catga");
        activity?.SetTag("messaging.operation", "process");
        activity?.SetTag("messaging.message_id", request.MessageId);
        activity?.SetTag("messaging.correlation_id", request.CorrelationId);
        activity?.SetTag("catga.message_type", requestType);
        activity?.SetTag("catga.request_type", typeof(TRequest).FullName);
        activity?.SetTag("catga.response_type", typeof(TResponse).FullName);
        activity?.SetTag("catga.timestamp", request.CreatedAt);

        try
        {
            var result = await next();
            var duration = Diagnostics.GetElapsedTime(startTime);
            activity?.SetTag("catga.success", result.IsSuccess);
            activity?.SetTag("catga.duration_ms", duration.TotalMilliseconds);

            if (result.IsSuccess)
                activity?.SetStatus(ActivityStatusCode.Ok);
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.Error);
                activity?.SetTag("catga.error_message", result.Error);
                if (result.Exception != null)
                {
                    activity?.SetTag("exception.type", result.Exception.GetType().Name);
                    activity?.SetTag("exception.message", result.Exception.Message);
                    activity?.SetTag("exception.stacktrace", result.Exception.StackTrace);
                    activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
                    {
                        ["exception.type"] = result.Exception.GetType().FullName,
                        ["exception.message"] = result.Exception.Message
                    }));
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            var duration = Diagnostics.GetElapsedTime(startTime);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().FullName);
            activity?.SetTag("exception.message", ex.Message);
            activity?.SetTag("exception.stacktrace", ex.StackTrace);
            activity?.SetTag("catga.duration_ms", duration.TotalMilliseconds);
            activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                ["exception.type"] = ex.GetType().FullName,
                ["exception.message"] = ex.Message,
                ["exception.escaped"] = true
            }));
            throw;
        }
    }
}

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

