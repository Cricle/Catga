using Catga.Abstractions;
using Catga.Core;
using Catga.Pipeline;
using Microsoft.Extensions.Logging;

namespace OrderSystem.Api.Behaviors;

/// <summary>
/// Pipeline behavior that validates requests before processing.
/// Demonstrates how to implement cross-cutting concerns.
/// </summary>
public class ValidationBehavior<TRequest, TResponse>(
    ILogger<ValidationBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var requestName = typeof(TRequest).Name;

        // Pre-processing: validation
        logger.LogDebug("Validating {Request}", requestName);

        // Example validation (extend with FluentValidation if needed)
        if (request is null)
            return CatgaResult<TResponse>.Failure("Request cannot be null");

        // Execute handler
        var result = await next();

        // Post-processing: logging
        if (result.IsSuccess)
            logger.LogDebug("{Request} completed successfully", requestName);
        else
            logger.LogWarning("{Request} failed: {Error}", requestName, result.Error);

        return result;
    }
}

/// <summary>
/// Pipeline behavior that logs request timing.
/// </summary>
public class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var requestName = typeof(TRequest).Name;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        logger.LogInformation("Handling {Request}", requestName);

        var result = await next();

        sw.Stop();
        logger.LogInformation("{Request} completed in {ElapsedMs}ms", requestName, sw.ElapsedMilliseconds);

        return result;
    }
}
