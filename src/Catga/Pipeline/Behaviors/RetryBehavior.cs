using Catga.Configuration;
using Catga.Core;
using Catga.Exceptions;
using Catga.Messages;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Catga.Pipeline.Behaviors;

/// <summary>Retry behavior with exponential backoff</summary>
public class RetryBehavior<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TRequest, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TResponse> : BaseBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    private readonly ResiliencePipeline _retryPipeline;

    public RetryBehavior(ILogger<RetryBehavior<TRequest, TResponse>> logger, CatgaOptions options) : base(logger)
    {
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
                    LogWarning("Retry {AttemptNumber}/{MaxAttempts} for {RequestType}", args.AttemptNumber, options.MaxRetryAttempts, GetRequestName());
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        try
        {
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
