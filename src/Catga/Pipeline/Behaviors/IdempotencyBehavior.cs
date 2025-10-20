using System.Diagnostics.CodeAnalysis;
using Catga.Core;
using Catga.Idempotency;
using Catga.Messages;
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
        if (string.IsNullOrEmpty(messageId)) return await next();

        // Check if already processed (optimize: removed unnecessary ResultMetadata allocation)
        if (await _store.HasBeenProcessedAsync(messageId, cancellationToken))
        {
            LogInformation("Message {MessageId} already processed - returning cached result", messageId);
            var cachedResult = await _store.GetCachedResultAsync<TResponse>(messageId, cancellationToken);
            return CatgaResult<TResponse>.Success(cachedResult ?? default!);  // No metadata allocation
        }

        // Process and cache successful results only (failed results not cached to allow retry)
        var result = await next();
        if (result.IsSuccess)
            await _store.MarkAsProcessedAsync(messageId, result.Value, cancellationToken);

        return result;
    }
}
