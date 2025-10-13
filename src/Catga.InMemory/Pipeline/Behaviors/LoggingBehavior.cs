using System.Diagnostics;
using Catga.Core;
using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>Structured logging behavior</summary>
public partial class LoggingBehavior<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TRequest, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TResponse> : BaseBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger) : base(logger) { }

    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var reqName = GetRequestName();
        var msgId = TryGetMessageId(request) ?? "N/A";
        var corrId = TryGetCorrelationId(request) ?? string.Empty;
        LogRequestStarted(reqName, msgId, corrId);

        try
        {
            var result = await next();
            sw.Stop();
            var duration = sw.ElapsedMilliseconds;
            if (result.IsSuccess)
                LogRequestSucceeded(reqName, msgId, duration, corrId);
            else
                LogRequestFailed(reqName, msgId, duration, result.Error ?? "Unknown error", corrId,
                    result.Exception != null ? ExceptionTypeCache.GetTypeName(result.Exception) : null);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            LogRequestException(ex, reqName, msgId, sw.ElapsedMilliseconds, corrId);
            throw;
        }
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Request started {RequestType} [MessageId={MessageId}, CorrelationId={CorrelationId}]")]
    partial void LogRequestStarted(string requestType, string messageId, string correlationId);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Request succeeded {RequestType} [MessageId={MessageId}, Duration={DurationMs}ms, CorrelationId={CorrelationId}]")]
    partial void LogRequestSucceeded(string requestType, string messageId, long durationMs, string correlationId);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Request failed {RequestType} [MessageId={MessageId}, Duration={DurationMs}ms, Error={Error}, CorrelationId={CorrelationId}, ErrorType={ErrorType}]")]
    partial void LogRequestFailed(string requestType, string messageId, long durationMs, string error, string correlationId, string? errorType);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Error, Message = "Request exception {RequestType} [MessageId={MessageId}, Duration={DurationMs}ms, CorrelationId={CorrelationId}]")]
    partial void LogRequestException(Exception exception, string requestType, string messageId, long durationMs, string correlationId);
}
