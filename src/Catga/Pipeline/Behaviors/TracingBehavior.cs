using System.Diagnostics;
using Catga.Messages;
using Catga.Results;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// 分布式追踪行为 - 使用 ActivitySource（100% AOT，非阻塞）
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
        using var activity = ActivitySource.StartActivity($"Catga.{typeof(TRequest).Name}");

        activity?.SetTag("catga.message_id", request.MessageId);
        activity?.SetTag("catga.correlation_id", request.CorrelationId);
        activity?.SetTag("catga.message_type", typeof(TRequest).Name);

        try
        {
            var result = await next();

            activity?.SetTag("catga.success", result.IsSuccess);
            if (!result.IsSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.Error);
                activity?.SetTag("catga.error", result.Error);
            }

            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().Name);
            throw;
        }
    }
}

