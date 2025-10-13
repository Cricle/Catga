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
        var messageId = TryGetMessageId(request);

        // Skip idempotency for requests without MessageId
        if (string.IsNullOrEmpty(messageId))
            return await next();

        // Check if already processed
        if (await _store.HasBeenProcessedAsync(messageId, cancellationToken))
        {
            LogInformation("Message {MessageId} already processed - returning cached result", messageId);
            var cachedResult = await _store.GetCachedResultAsync<TResponse>(messageId, cancellationToken);
            var metadata = ResultMetadata.Create();
            metadata.Add("FromCache", "true");
            metadata.Add("MessageId", messageId);
            return CatgaResult<TResponse>.Success(cachedResult ?? default!, metadata);
        }

        // Process the request
        var result = await next();

        // Store result for idempotency (both success and failure)
        // Only store if result has a value or if it's a successful operation
        if (result.IsSuccess && result.Value != null)
        {
            await _store.MarkAsProcessedAsync(messageId, result.Value, cancellationToken);
        }
        else if (result.IsSuccess)
        {
            // Success with no value (void/Unit result)
            await _store.MarkAsProcessedAsync<TResponse>(messageId, default, cancellationToken);
        }
        // Note: Failed results are NOT cached to allow retry

        return result;
    }
}
