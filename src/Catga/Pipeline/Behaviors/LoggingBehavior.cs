using System.Diagnostics;
using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// 结构化日志记录行为（高性能、完整上下文）
/// </summary>
public partial class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        Func<Task<CatgaResult<TResponse>>> next,
        CancellationToken cancellationToken = default)
    {
        var requestName = typeof(TRequest).Name;
        var sw = Stopwatch.StartNew();

        // 使用源生成的日志方法（AOT 兼容 + 高性能）
        LogRequestStarted(requestName, request.MessageId, request.CorrelationId ?? string.Empty);

        try
        {
            var result = await next();
            sw.Stop();

            if (result.IsSuccess)
            {
                LogRequestSucceeded(
                    requestName,
                    request.MessageId,
                    sw.ElapsedMilliseconds,
                    request.CorrelationId ?? string.Empty);
            }
            else
            {
                LogRequestFailed(
                    requestName,
                    request.MessageId,
                    sw.ElapsedMilliseconds,
                    result.Error ?? "Unknown error",
                    request.CorrelationId ?? string.Empty,
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
                request.MessageId,
                sw.ElapsedMilliseconds,
                request.CorrelationId ?? string.Empty);
            throw;
        }
    }

    // 源生成日志方法（AOT 兼容 + 零分配）
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "处理请求开始 {RequestType} [MessageId={MessageId}, CorrelationId={CorrelationId}]")]
    partial void LogRequestStarted(string requestType, string messageId, string correlationId);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "请求成功 {RequestType} [MessageId={MessageId}, Duration={DurationMs}ms, CorrelationId={CorrelationId}]")]
    partial void LogRequestSucceeded(
        string requestType,
        string messageId,
        long durationMs,
        string correlationId);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Warning,
        Message = "请求失败 {RequestType} [MessageId={MessageId}, Duration={DurationMs}ms, Error={Error}, CorrelationId={CorrelationId}, ErrorType={ErrorType}]")]
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
        Message = "请求异常 {RequestType} [MessageId={MessageId}, Duration={DurationMs}ms, CorrelationId={CorrelationId}]")]
    partial void LogRequestException(
        Exception exception,
        string requestType,
        string messageId,
        long durationMs,
        string correlationId);
}
