using Catga.Configuration;
using Catga.Core;
using Catga.Exceptions;
using Catga.Abstractions;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System.Diagnostics.CodeAnalysis;

namespace Catga.Pipeline.Behaviors;

/// <summary>Retry behavior with exponential backoff using Polly.</summary>
public partial class RetryBehavior<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse> : BaseBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    private readonly ResiliencePipeline<CatgaResult<TResponse>> _pipeline;

    public RetryBehavior(ILogger<RetryBehavior<TRequest, TResponse>> logger, CatgaOptions options) : base(logger)
    {
        _pipeline = new ResiliencePipelineBuilder<CatgaResult<TResponse>>()
            .AddRetry(new RetryStrategyOptions<CatgaResult<TResponse>>
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                Delay = TimeSpan.FromMilliseconds(options.RetryDelayMs),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<CatgaResult<TResponse>>()
                    .Handle<CatgaException>(ex => ex.IsRetryable)
                    .HandleResult(r => !r.IsSuccess && r.Exception is CatgaException { IsRetryable: true }),
                OnRetry = args =>
                {
                    LogRetry(logger, args.AttemptNumber, options.MaxRetryAttempts, GetRequestName());
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _pipeline.ExecuteAsync(async ct => await next(), cancellationToken);
        }
        catch (CatgaException ex)
        {
            LogRequestFailed(Logger, ex, GetRequestName());
            return CatgaResult<TResponse>.Failure(ex.Message, ex);
        }
        catch (Exception ex)
        {
            return CatgaResult<TResponse>.Failure("Unexpected error", new CatgaException("Unexpected error", ex));
        }
    }

    [LoggerMessage(Message = "Retry {attemptNumber}/{maxAttempts} for {requestType}", Level = LogLevel.Warning)]
    static partial void LogRetry(ILogger logger, int attemptNumber, int maxAttempts, string requestType);

    [LoggerMessage(Message = "Request failed after retries: {requestType}", Level = LogLevel.Error)]
    static partial void LogRequestFailed(ILogger logger, Exception ex, string requestType);
}
