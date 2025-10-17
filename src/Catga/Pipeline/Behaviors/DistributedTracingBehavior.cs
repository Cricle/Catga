using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Catga.Messages;
using Catga.Observability;
using Catga.Results;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Pipeline behavior that creates rich distributed traces for Jaeger/Zipkin
/// Shows complete message flow, events, and execution details
/// </summary>
public sealed class DistributedTracingBehavior<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse> : IPipelineBehavior<TRequest, TResponse>
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

        // Capture request payload for Jaeger UI (debug-only, graceful degradation on AOT)
        ActivityPayloadCapture.CaptureRequest(activity, request);

        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            // Execute pipeline
            var result = await next();

            var durationMs = GetElapsedMilliseconds(startTimestamp);

            // Set success tags
            activity.SetSuccess(result.IsSuccess);
            activity.SetTag(CatgaActivitySource.Tags.Duration, durationMs);

            // Add result details
            if (result.IsSuccess)
            {
                activity.AddActivityEvent("Command.Succeeded",
                    ("Duration", $"{durationMs:F2}ms"));

                // Capture response payload for Jaeger UI (debug-only, graceful degradation on AOT)
                ActivityPayloadCapture.CaptureResponse(activity, result.Value);
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
            var durationMs = GetElapsedMilliseconds(startTimestamp);

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

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static double GetElapsedMilliseconds(long startTimestamp)
    {
        var elapsed = Stopwatch.GetTimestamp() - startTimestamp;
        return elapsed * 1000.0 / Stopwatch.Frequency;
    }

    private static string GetCorrelationId(TRequest request)
    {
        // 1. Try Activity.Current baggage (AOT-safe, distributed tracing standard)
        var baggageId = Activity.Current?.GetBaggageItem("catga.correlation_id");
        if (!string.IsNullOrEmpty(baggageId))
            return baggageId;

        // 2. Try IMessage interface (AOT-safe, type-safe contract)
        if (request is IMessage message && !string.IsNullOrEmpty(message.CorrelationId))
            return message.CorrelationId;

        // 3. Generate new correlation ID
        // Note: If you need HTTP-level correlation propagation, ensure:
        // - CorrelationIdDelegatingHandler is registered (for outgoing requests)
        // - ASP.NET Core middleware sets Activity.Baggage (for incoming requests)
        return Guid.NewGuid().ToString("N");
    }
}

