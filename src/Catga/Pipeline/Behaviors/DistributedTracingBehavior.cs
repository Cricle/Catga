using System.Diagnostics;
using Catga.Messages;
using Catga.Observability;
using Catga.Results;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Pipeline behavior that creates rich distributed traces for Jaeger/Zipkin
/// Shows complete message flow, events, and execution details
/// </summary>
public sealed class DistributedTracingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : class, IRequest<TResponse>
{
    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var requestType = typeof(TRequest).Name;
        var activityName = $"Catga.Handle.{requestType}";

        using var activity = CatgaActivitySource.Source.StartActivity(
            activityName,
            ActivityKind.Internal);

        if (activity == null)
        {
            // Tracing disabled, skip
            return await next();
        }

        // Set rich tags for Jaeger UI
        var correlationId = GetCorrelationId(request);
        activity.SetTag(CatgaActivitySource.Tags.RequestType, requestType);
        activity.SetTag(CatgaActivitySource.Tags.MessageType, requestType);
        activity.SetTag(CatgaActivitySource.Tags.CorrelationId, correlationId);

        // Add correlation ID to baggage for distributed context
        activity.SetBaggage(CatgaActivitySource.Tags.CorrelationId, correlationId);

        // Add message details as events
        if (request is IMessage message)
        {
            activity.SetTag(CatgaActivitySource.Tags.MessageId, message.MessageId);
            activity.AddActivityEvent("Message.Received",
                ("MessageId", message.MessageId),
                ("CorrelationId", message.CorrelationId));
        }

        // Capture request payload (serialized)
        try
        {
            var requestJson = System.Text.Json.JsonSerializer.Serialize(request);
            if (requestJson.Length < 4096) // Limit size
            {
                activity.SetTag("catga.request.payload", requestJson);
            }
            else
            {
                activity.SetTag("catga.request.payload", $"<too large: {requestJson.Length} bytes>");
            }
        }
        catch
        {
            // Ignore serialization errors
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Execute pipeline
            var result = await next();

            stopwatch.Stop();
            var durationMs = stopwatch.Elapsed.TotalMilliseconds;

            // Set success tags
            activity.SetSuccess(result.IsSuccess);
            activity.SetTag(CatgaActivitySource.Tags.Duration, durationMs);

            // Add result details
            if (result.IsSuccess)
            {
                activity.AddActivityEvent("Command.Succeeded",
                    ("Duration", $"{durationMs:F2}ms"));

                // Capture response payload
                if (result.Value != null)
                {
                    try
                    {
                        var responseJson = System.Text.Json.JsonSerializer.Serialize(result.Value);
                        if (responseJson.Length < 4096)
                        {
                            activity.SetTag("catga.response.payload", responseJson);
                        }
                    }
                    catch
                    {
                        // Ignore serialization errors
                    }
                }
            }
            else
            {
                activity.AddActivityEvent("Command.Failed",
                    ("Error", result.Error ?? "Unknown error"),
                    ("Duration", $"{durationMs:F2}ms"));

                activity.SetTag(CatgaActivitySource.Tags.Error, result.Error);

                if (result.Exception != null)
                {
                    activity.SetError(result.Exception);
                }
                else
                {
                    activity.SetStatus(ActivityStatusCode.Error, result.Error);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var durationMs = stopwatch.Elapsed.TotalMilliseconds;

            // Record exception
            activity.SetError(ex);
            activity.SetTag(CatgaActivitySource.Tags.Duration, durationMs);
            activity.AddActivityEvent("Command.Exception",
                ("ExceptionType", ex.GetType().Name),
                ("Message", ex.Message),
                ("Duration", $"{durationMs:F2}ms"));

            throw;
        }
    }

    private static string GetCorrelationId(TRequest request)
    {
        // Try to get from global middleware
        try
        {
            var middlewareType = Type.GetType("Catga.AspNetCore.Middleware.CorrelationIdMiddleware, Catga.AspNetCore");
            if (middlewareType != null)
            {
                var currentProperty = middlewareType.GetProperty("Current", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var globalId = currentProperty?.GetValue(null) as string;
                if (!string.IsNullOrEmpty(globalId))
                    return globalId;
            }
        }
        catch { }

        // Try to get from message
        if (request is IMessage message && !string.IsNullOrEmpty(message.CorrelationId))
            return message.CorrelationId;

        // Generate new
        return Guid.NewGuid().ToString("N");
    }
}

