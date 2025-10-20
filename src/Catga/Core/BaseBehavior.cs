using Catga.DistributedId;
using Catga.Messages;
using Catga.Pipeline;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace Catga.Core;

/// <summary>Base class for pipeline behaviors (AOT-compatible)</summary>
public abstract class BaseBehavior<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    protected readonly ILogger Logger;

    protected BaseBehavior(ILogger logger) => Logger = logger;

    public abstract ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default);

    protected static string GetRequestName() => TypeNameCache<TRequest>.Name;
    protected static string GetRequestFullName() => TypeNameCache<TRequest>.FullName;
    protected static string GetResponseName() => TypeNameCache<TResponse>.Name;

    protected static long? TryGetMessageId(TRequest request)
        => request is IMessage message && message.MessageId != 0 ? message.MessageId : null;

    protected static long? TryGetCorrelationId(TRequest request)
        => request is IMessage message ? message.CorrelationId : null;

    protected static long GetCorrelationId(TRequest request, IDistributedIdGenerator idGenerator)
    {
        var correlationId = TryGetCorrelationId(request);
        return correlationId ?? idGenerator.NextId();
    }

    protected void LogSuccess(long messageId, long durationMs)
        => Logger.LogDebug("{BehaviorType} succeeded for {RequestType} [MessageId={MessageId}, Duration={Duration}ms]",
            GetType().Name, GetRequestName(), messageId, durationMs);

    protected void LogFailure(long messageId, Exception ex)
        => Logger.LogError(ex, "{BehaviorType} failed for {RequestType} [MessageId={MessageId}]",
            GetType().Name, GetRequestName(), messageId);

    protected void LogWarning(string message, params object[] args) => Logger.LogWarning(message, args);
    protected void LogInformation(string message, params object[] args) => Logger.LogInformation(message, args);
}
