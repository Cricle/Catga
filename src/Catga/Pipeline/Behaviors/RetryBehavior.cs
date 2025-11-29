using Catga.Configuration;
using Catga.Core;
using Catga.Exceptions;
using Catga.Abstractions;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System.Diagnostics.CodeAnalysis;

namespace Catga.Pipeline.Behaviors;

/// <summary>Retry behavior with exponential backoff</summary>
public partial class RetryBehavior<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse> : BaseBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    private readonly ResiliencePipeline _retryPipeline;
    private readonly CatgaOptions _options;

    public RetryBehavior(ILogger<RetryBehavior<TRequest, TResponse>> logger, CatgaOptions options) : base(logger)
    {
        _options = options;
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
                    LogRetry(logger, args.AttemptNumber, options.MaxRetryAttempts, GetRequestName());
                    var ex = args.Outcome.Exception ?? new CatgaException("Retrying request");
                    object state = $"Retry {args.AttemptNumber}/{options.MaxRetryAttempts} for {GetRequestName()}";
                    logger.Log(LogLevel.Warning, default, state, ex, static (object s, Exception? e) => s?.ToString() ?? string.Empty);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        var max = _options.MaxRetryAttempts;
        var attempt = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;
            try
            {
                return await next();
            }
            catch (CatgaException ex)
            {
                if (!ex.IsRetryable)
                {
                    LogRequestFailed(Logger, ex, GetRequestName());
                    return CatgaResult<TResponse>.Failure(ex.Message, ex);
                }

                if (attempt > max)
                {
                    LogRequestFailed(Logger, ex, GetRequestName());
                    return CatgaResult<TResponse>.Failure(ex.Message, ex);
                }

                LogRetry(Logger, attempt, max, GetRequestName());
                Logger.Log(LogLevel.Warning, default, $"Retry {attempt}/{max} for {GetRequestName()}", ex, static (object s, Exception? e) => s?.ToString() ?? string.Empty);

                if (_options.RetryDelayMs > 0)
                    await Task.Delay(_options.RetryDelayMs, cancellationToken);
            }
            catch (Exception ex)
            {
                return CatgaResult<TResponse>.Failure("Unexpected error", new CatgaException("Unexpected error", ex));
            }
        }
    }

    [LoggerMessage(Message = "Retry {attemptNumber}/{maxAttempts} for {requestType}", Level = LogLevel.Warning)]
    static partial void LogRetry(ILogger logger, int attemptNumber, int maxAttempts, string requestType);

    [LoggerMessage(Message = "Request failed after retries: {requestType}", Level = LogLevel.Error)]
    static partial void LogRequestFailed(ILogger logger, Exception ex, string requestType);
}
