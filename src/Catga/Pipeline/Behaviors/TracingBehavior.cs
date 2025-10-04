using System.Diagnostics;
using Catga.Messages;
using Catga.Results;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Distributed tracing behavior using ActivitySource (100% AOT, non-blocking)
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
        using var activity = ActivitySource.StartActivity(
            $"Transit.{typeof(TRequest).Name}",
            ActivityKind.Internal);

        if (activity != null)
        {
            activity.SetTag("transit.message_id", request.MessageId);
            activity.SetTag("transit.correlation_id", request.CorrelationId);
            activity.SetTag("transit.message_type", typeof(TRequest).Name);
            activity.SetTag("transit.response_type", typeof(TResponse).Name);
        }

        try
        {
            var result = await next();

            if (activity != null)
            {
                activity.SetTag("transit.success", result.IsSuccess);
                if (!result.IsSuccess)
                {
                    activity.SetStatus(ActivityStatusCode.Error, result.Error);
                    activity.SetTag("transit.error", result.Error);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().Name);
            activity?.SetTag("exception.message", ex.Message);
            activity?.SetTag("exception.stacktrace", ex.StackTrace);
            throw;
        }
    }
}

