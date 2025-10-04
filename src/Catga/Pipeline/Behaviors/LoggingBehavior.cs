using System.Diagnostics;
using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Simplified logging behavior (non-blocking)
/// </summary>
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TransitResult<TResponse>> HandleAsync(
        TRequest request,
        Func<Task<TransitResult<TResponse>>> next,
        CancellationToken cancellationToken = default)
    {
        var requestName = typeof(TRequest).Name;
        var sw = Stopwatch.StartNew();

        _logger.LogInformation("Handling {Request} {MessageId}", requestName, request.MessageId);

        try
        {
            var result = await next();

            _logger.Log(
                result.IsSuccess ? LogLevel.Information : LogLevel.Warning,
                "{Request} {Status} in {Ms}ms {MessageId} {Error}",
                requestName,
                result.IsSuccess ? "succeeded" : "failed",
                sw.ElapsedMilliseconds,
                request.MessageId,
                result.Error ?? "");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Request} exception in {Ms}ms {MessageId}",
                requestName, sw.ElapsedMilliseconds, request.MessageId);
            throw;
        }
    }
}
