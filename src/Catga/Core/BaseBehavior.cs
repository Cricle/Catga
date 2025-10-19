using System.Diagnostics.CodeAnalysis;
using Catga.DistributedId;
using Catga.Messages;
using Catga.Pipeline;
using Microsoft.Extensions.Logging;

namespace Catga.Core;

/// <summary>Base class for pipeline behaviors (AOT-compatible)</summary>
public abstract class BaseBehavior<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    protected readonly ILogger Logger;

    protected BaseBehavior(ILogger logger) => Logger = logger;

    public abstract ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken cancellationToken = default);

    protected static string GetRequestName() => TypeNameCache<TRequest>.Name;
    protected static string GetRequestFullName() => TypeNameCache<TRequest>.FullName;
    protected static string GetResponseName() => TypeNameCache<TResponse>.Name;

    protected static string? TryGetMessageId(TRequest request)
        => request is IMessage message && !string.IsNullOrEmpty(message.MessageId) ? message.MessageId : null;

    protected static string? TryGetCorrelationId(TRequest request)
        => request is IMessage message ? message.CorrelationId : null;

    protected static string GetCorrelationId(TRequest request, IDistributedIdGenerator idGenerator)
    {
        var correlationId = TryGetCorrelationId(request);
        return correlationId ?? idGenerator.NextId().ToString();
    }

    protected async ValueTask<CatgaResult<TResponse>> SafeExecuteAsync(TRequest request, PipelineDelegate<TResponse> next, Func<TRequest, PipelineDelegate<TResponse>, ValueTask<CatgaResult<TResponse>>> handler, CancellationToken cancellationToken = default)
    {
        try
        {
            return await handler(request, next);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in {BehaviorType} for {RequestType}", GetType().Name, GetRequestName());
            throw;
        }
    }

    protected void LogSuccess(string messageId, long durationMs)
        => Logger.LogDebug("{BehaviorType} succeeded for {RequestType} [MessageId={MessageId}, Duration={Duration}ms]", GetType().Name, GetRequestName(), messageId, durationMs);

    protected void LogFailure(string messageId, Exception ex)
        => Logger.LogError(ex, "{BehaviorType} failed for {RequestType} [MessageId={MessageId}]", GetType().Name, GetRequestName(), messageId);

    protected void LogWarning(string message, params object[] args) => Logger.LogWarning(message, args);

    protected void LogInformation(string message, params object[] args) => Logger.LogInformation(message, args);

    protected static bool IsEvent(TRequest request) => request is IEvent;
}

