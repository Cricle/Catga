using System.Diagnostics;
using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Structured logging behavior (High performance, Full context)
/// </summary>
public partial class LoggingBehavior<TRequest, TResponse> : BaseBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
        : base(logger)
    {
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
        var requestName = GetRequestName();
        var messageId = TryGetMessageId(request) ?? "N/A";
        var correlationId = TryGetCorrelationId(request) ?? string.Empty;
        var sw = Stopwatch.StartNew();

        // Use source-generated logging methods (AOT compatible + High performance)
        LogRequestStarted(requestName, messageId, correlationId);

        try
        {
            var result = await next();
            sw.Stop();

            if (result.IsSuccess)
            {
                LogRequestSucceeded(
                    requestName,
                    messageId,
                    sw.ElapsedMilliseconds,
                    correlationId);
            }
            else
            {
                LogRequestFailed(
                    requestName,
                    messageId,
                    sw.ElapsedMilliseconds,
                    result.Error ?? "Unknown error",
                    correlationId,
                    result.Exception?.GetType().Name);
            }

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            LogRequestException(
                ex,
                requestName,
                messageId,
                sw.ElapsedMilliseconds,
                correlationId);
            throw;
        }
    }

    // Source-generated logging methods (AOT compatible + Zero allocations)
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Request started {RequestType} [MessageId={MessageId}, CorrelationId={CorrelationId}]")]
    partial void LogRequestStarted(string requestType, string messageId, string correlationId);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Request succeeded {RequestType} [MessageId={MessageId}, Duration={DurationMs}ms, CorrelationId={CorrelationId}]")]
    partial void LogRequestSucceeded(
        string requestType,
        string messageId,
        long durationMs,
        string correlationId);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Warning,
        Message = "Request failed {RequestType} [MessageId={MessageId}, Duration={DurationMs}ms, Error={Error}, CorrelationId={CorrelationId}, ErrorType={ErrorType}]")]
    partial void LogRequestFailed(
        string requestType,
        string messageId,
        long durationMs,
        string error,
        string correlationId,
        string? errorType);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Error,
        Message = "Request exception {RequestType} [MessageId={MessageId}, Duration={DurationMs}ms, CorrelationId={CorrelationId}]")]
    partial void LogRequestException(
        Exception exception,
        string requestType,
        string messageId,
        long durationMs,
        string correlationId);
}
