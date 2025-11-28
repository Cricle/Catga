using System.Diagnostics.CodeAnalysis;
using Catga.DistributedId;
using Catga.Abstractions;
using Catga.Pipeline;
using Microsoft.Extensions.Logging;

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

    protected static long GetCorrelationId(TRequest request, IDistributedIdGenerator idGenerator) => TryGetCorrelationId(request) ?? idGenerator.NextId();
}
