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
public class RetryBehavior<TRequest, TResponse> : BaseBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ResiliencePipeline _retryPipeline;

    public RetryBehavior(
        ILogger<RetryBehavior<TRequest, TResponse>> logger,
        CatgaOptions options)
        : base(logger)
    {
        // Build retry pipeline from options
        _retryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                Delay = TimeSpan.FromMilliseconds(options.RetryDelayMs),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder().Handle<CatgaException>(ex => ex.IsRetryable),
                OnRetry = args =>
                {
                    LogWarning(
                        "Retry {AttemptNumber}/{MaxAttempts} for {RequestType}",
                        args.AttemptNumber, options.MaxRetryAttempts, GetRequestName());
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    /// <summary>
    /// Optimized: Use ValueTask to reduce heap allocations
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Pipeline behaviors may require types that cannot be statically analyzed.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Pipeline behaviors use reflection for handler resolution.")]
    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Simplified retry logic - use Polly pipeline directly
            return await _retryPipeline.ExecuteAsync(async ct => await next(), cancellationToken);
        }
        catch (CatgaException ex)
        {
            Logger.LogError(ex, "Request failed after retries: {RequestType}", GetRequestName());
            return CatgaResult<TResponse>.Failure(ex.Message, ex);
        }
        catch (Exception ex)
        {
            return CatgaResult<TResponse>.Failure("Unexpected error", new CatgaException("Unexpected error", ex));
        }
    }
}
