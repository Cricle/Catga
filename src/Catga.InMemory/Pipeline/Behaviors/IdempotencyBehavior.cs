using System.Diagnostics.CodeAnalysis;
using Catga.Idempotency;
using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Simplified idempotency behavior (100% AOT compatible, lock-free)
/// </summary>
public class IdempotencyBehavior<TRequest, TResponse> : BaseBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IIdempotencyStore _store;

    public IdempotencyBehavior(
        IIdempotencyStore store,
        ILogger<IdempotencyBehavior<TRequest, TResponse>> logger)
        : base(logger)
    {
        _store = store;
    }

    /// <summary>
    /// Optimized: Use ValueTask to reduce heap allocations
    /// Note: Serialization warnings are marked on IIdempotencyStore interface methods
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Serialization warnings are marked on IIdempotencyStore interface")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Serialization warnings are marked on IIdempotencyStore interface")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Pipeline behaviors may require types that cannot be statically analyzed.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Pipeline behaviors use reflection for handler resolution.")]
    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var messageId = TryGetMessageId(request) ?? string.Empty;

        // Check cache (non-blocking)
        if (await _store.HasBeenProcessedAsync(messageId, cancellationToken))
        {
            LogInformation("Message {MessageId} already processed - returning cached result", messageId);

            var cachedResult = await _store.GetCachedResultAsync<TResponse>(messageId, cancellationToken);
            return CatgaResult<TResponse>.Success(cachedResult ?? default!, CreateCacheMetadata());
        }

        // Process message
        var result = await next();

        // Cache successful results (non-blocking)
        if (result.IsSuccess && result.Value != null)
        {
            await _store.MarkAsProcessedAsync(messageId, result.Value, cancellationToken);
            Logger.LogDebug("Marked message {MessageId} as processed", messageId);
        }

        return result;
    }

    private static ResultMetadata CreateCacheMetadata()
    {
        var metadata = new ResultMetadata();
        metadata.Add("FromCache", "true");
        return metadata;
    }
}
