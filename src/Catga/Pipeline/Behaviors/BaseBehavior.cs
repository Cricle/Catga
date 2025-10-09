using Catga.Common;
using Catga.DistributedId;
using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Base class for all pipeline behaviors
/// Provides common utilities and reduces code duplication (DRY principle)
/// </summary>
public abstract class BaseBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    protected readonly ILogger Logger;

    protected BaseBehavior(ILogger logger)
    {
        Logger = logger;
    }

    public abstract ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get request type name
    /// </summary>
    protected static string GetRequestName() => typeof(TRequest).Name;

    /// <summary>
    /// Get request full type name
    /// </summary>
    protected static string GetRequestFullName() => typeof(TRequest).FullName ?? typeof(TRequest).Name;

    /// <summary>
    /// Get response type name
    /// </summary>
    protected static string GetResponseName() => typeof(TResponse).Name;

    /// <summary>
    /// Get MessageId from request (safe extraction)
    /// </summary>
    protected static string? TryGetMessageId(TRequest request)
    {
        return request is IMessage message && !string.IsNullOrEmpty(message.MessageId)
            ? message.MessageId
            : null;
    }

    /// <summary>
    /// Get or generate MessageId
    /// </summary>
    protected static string GetMessageId(TRequest request, IDistributedIdGenerator idGenerator)
    {
        return MessageHelper.GetOrGenerateMessageId(request, idGenerator);
    }

    /// <summary>
    /// Get CorrelationId from request (safe extraction)
    /// </summary>
    protected static string? TryGetCorrelationId(TRequest request)
    {
        return MessageHelper.GetCorrelationId(request);
    }

    /// <summary>
    /// Get or generate CorrelationId
    /// </summary>
    protected static string GetCorrelationId(TRequest request, IDistributedIdGenerator idGenerator)
    {
        var correlationId = TryGetCorrelationId(request);
        return correlationId ?? idGenerator.NextId().ToString();
    }

    /// <summary>
    /// Safe execute with automatic exception handling and logging
    /// </summary>
    protected async ValueTask<CatgaResult<TResponse>> SafeExecuteAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        Func<TRequest, PipelineDelegate<TResponse>, ValueTask<CatgaResult<TResponse>>> handler,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await handler(request, next);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Error in {BehaviorType} for {RequestType}",
                GetType().Name,
                GetRequestName());
            throw;
        }
    }

    /// <summary>
    /// Log operation success
    /// </summary>
    protected void LogSuccess(string messageId, long durationMs)
    {
        Logger.LogDebug(
            "{BehaviorType} succeeded for {RequestType} [MessageId={MessageId}, Duration={Duration}ms]",
            GetType().Name,
            GetRequestName(),
            messageId,
            durationMs);
    }

    /// <summary>
    /// Log operation failure
    /// </summary>
    protected void LogFailure(string messageId, Exception ex)
    {
        Logger.LogError(ex,
            "{BehaviorType} failed for {RequestType} [MessageId={MessageId}]",
            GetType().Name,
            GetRequestName(),
            messageId);
    }

    /// <summary>
    /// Log operation warning
    /// </summary>
    protected void LogWarning(string message, params object[] args)
    {
        Logger.LogWarning(message, args);
    }

    /// <summary>
    /// Log operation info
    /// </summary>
    protected void LogInformation(string message, params object[] args)
    {
        Logger.LogInformation(message, args);
    }

    /// <summary>
    /// Check if request is an event
    /// </summary>
    protected static bool IsEvent(TRequest request) => request is IEvent;

    /// <summary>
    /// Check if request is a command
    /// </summary>
    protected static bool IsCommand(TRequest request) => request is ICommand;

    /// <summary>
    /// Check if request is a query
    /// </summary>
    protected static bool IsQuery(TRequest request) => request is IQuery<TResponse>;
}

