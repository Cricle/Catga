using System.Diagnostics.CodeAnalysis;
using Catga.Idempotency;
using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>Idempotency behavior</summary>
public class IdempotencyBehavior<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse> : BaseBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    private readonly IIdempotencyStore _store;

    public IdempotencyBehavior(IIdempotencyStore store, ILogger<IdempotencyBehavior<TRequest, TResponse>> logger) : base(logger)
        => _store = store;

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Serialization warnings are marked on IIdempotencyStore interface")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Serialization warnings are marked on IIdempotencyStore interface")]
    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        var messageId = TryGetMessageId(request) ?? string.Empty;
        if (await _store.HasBeenProcessedAsync(messageId, cancellationToken))
        {
            LogInformation("Message {MessageId} already processed - returning cached result", messageId);
            var cachedResult = await _store.GetCachedResultAsync<TResponse>(messageId, cancellationToken);
            var metadata = new ResultMetadata();
            metadata.Add("FromCache", "true");
            return CatgaResult<TResponse>.Success(cachedResult ?? default!, metadata);
        }

        var result = await next();
        if (result.IsSuccess && result.Value != null)
            await _store.MarkAsProcessedAsync(messageId, result.Value, cancellationToken);
        return result;
    }
}
