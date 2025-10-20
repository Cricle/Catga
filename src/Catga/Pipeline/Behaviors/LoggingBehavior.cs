using System.Diagnostics;
using Catga.Core;
using Catga.Messages;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>Structured logging behavior</summary>
public partial class LoggingBehavior<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TRequest, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TResponse> : BaseBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger) : base(logger) { }

    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var reqName = GetRequestName();
        var msgId = TryGetMessageId(request) ?? 0;  // Default to 0 for "N/A"
        var corrId = TryGetCorrelationId(request) ?? 0;  // Default to 0 for empty
        LogRequestStarted(reqName, msgId, corrId);

        try
        {
            var result = await next();
            var duration = (long)GetElapsedMilliseconds(startTimestamp);
            if (result.IsSuccess)
                LogRequestSucceeded(reqName, msgId, duration, corrId);
            else
                LogRequestFailed(reqName, msgId, duration, result.Error ?? "Unknown error", corrId,
                    result.Exception != null ? ExceptionTypeCache.GetTypeName(result.Exception) : null);
            return result;
        }
        catch (Exception ex)
        {
            LogRequestException(ex, reqName, msgId, (long)GetElapsedMilliseconds(startTimestamp), corrId);
            throw;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static double GetElapsedMilliseconds(long startTimestamp)
    {
        var elapsed = Stopwatch.GetTimestamp() - startTimestamp;
        return elapsed * 1000.0 / Stopwatch.Frequency;
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Request started {RequestType} [MessageId={MessageId}, CorrelationId={CorrelationId}]")]
    partial void LogRequestStarted(string requestType, long messageId, long correlationId);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Request succeeded {RequestType} [MessageId={MessageId}, Duration={DurationMs}ms, CorrelationId={CorrelationId}]")]
    partial void LogRequestSucceeded(string requestType, long messageId, long durationMs, long correlationId);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Request failed {RequestType} [MessageId={MessageId}, Duration={DurationMs}ms, Error={Error}, CorrelationId={CorrelationId}, ErrorType={ErrorType}]")]
    partial void LogRequestFailed(string requestType, long messageId, long durationMs, string error, long correlationId, string? errorType);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Error, Message = "Request exception {RequestType} [MessageId={MessageId}, Duration={DurationMs}ms, CorrelationId={CorrelationId}]")]
    partial void LogRequestException(Exception exception, string requestType, long messageId, long durationMs, long correlationId);
}
