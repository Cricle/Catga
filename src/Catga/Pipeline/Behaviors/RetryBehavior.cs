using Catga.Configuration;
using Catga.Exceptions;
using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Simplified retry behavior with exponential backoff and jitter (AOT-compatible)
/// </summary>
public class RetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<RetryBehavior<TRequest, TResponse>> _logger;
    private readonly ResiliencePipeline _retryPipeline;

    public RetryBehavior(
        ILogger<RetryBehavior<TRequest, TResponse>> logger,
        TransitOptions options)
    {
        _logger = logger;

        // Build retry pipeline from options
        _retryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                Delay = TimeSpan.FromMilliseconds(options.RetryDelayMs),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder().Handle<TransitException>(ex => ex.IsRetryable),
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "Retry {AttemptNumber}/{MaxAttempts} for {RequestType}",
                        args.AttemptNumber, options.MaxRetryAttempts, typeof(TRequest).Name);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public async Task<TransitResult<TResponse>> HandleAsync(
        TRequest request,
        Func<Task<TransitResult<TResponse>>> next,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _retryPipeline.ExecuteAsync(async ct => await next(), cancellationToken);
        }
        catch (TransitException ex)
        {
            _logger.LogError(ex, "Request failed after retries: {RequestType}", typeof(TRequest).Name);
            return TransitResult<TResponse>.Failure(ex.Message, ex);
        }
        catch (Exception ex)
        {
            return TransitResult<TResponse>.Failure("Unexpected error", new TransitException("Unexpected error", ex));
        }
    }
}
