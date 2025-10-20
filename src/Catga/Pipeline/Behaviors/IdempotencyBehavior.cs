using System.Diagnostics.CodeAnalysis;
using Catga.Core;
using Catga.Idempotency;
using Catga.Abstractions;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>Idempotency behavior</summary>
public class IdempotencyBehavior<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse> : BaseBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    private readonly IIdempotencyStore _store;

    public IdempotencyBehavior(IIdempotencyStore store, ILogger<IdempotencyBehavior<TRequest, TResponse>> logger) : base(logger)
        => _store = store;

    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        var messageId = TryGetMessageId(request);
        if (!messageId.HasValue) return await next();  // No MessageId, skip idempotency

        var id = messageId.Value;

        // Check if already processed (optimize: removed unnecessary ResultMetadata allocation)
        if (await _store.HasBeenProcessedAsync(id, cancellationToken))
        {
            LogInformation("Message {MessageId} already processed - returning cached result", id);
            var cachedResult = await _store.GetCachedResultAsync<TResponse>(id, cancellationToken);
            return CatgaResult<TResponse>.Success(cachedResult ?? default!);  // No metadata allocation
        }

        // Process and cache successful results only (failed results not cached to allow retry)
        var result = await next();
        if (result.IsSuccess)
            await _store.MarkAsProcessedAsync(id, result.Value, cancellationToken);

        return result;
    }
}
