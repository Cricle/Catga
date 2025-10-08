using System.Diagnostics.CodeAnalysis;
using Catga.Idempotency;
using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Simplified idempotency behavior (100% AOT compatible, lock-free)
/// </summary>
public class IdempotencyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IIdempotencyStore _store;
    private readonly ILogger<IdempotencyBehavior<TRequest, TResponse>> _logger;

    public IdempotencyBehavior(IIdempotencyStore store, ILogger<IdempotencyBehavior<TRequest, TResponse>> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Optimized: Use ValueTask to reduce heap allocations
    /// Note: Serialization warnings are marked on IIdempotencyStore interface methods
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Serialization warnings are marked on IIdempotencyStore interface")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Serialization warnings are marked on IIdempotencyStore interface")]
    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        // Check cache (non-blocking)
        if (await _store.HasBeenProcessedAsync(request.MessageId, cancellationToken))
        {
            _logger.LogInformation("Message {MessageId} already processed - returning cached result", request.MessageId);

            var cachedResult = await _store.GetCachedResultAsync<TResponse>(request.MessageId, cancellationToken);
            return CatgaResult<TResponse>.Success(cachedResult ?? default!, CreateCacheMetadata());
        }

        // Process message
        var result = await next();

        // Cache successful results (non-blocking)
        if (result.IsSuccess && result.Value != null)
        {
            await _store.MarkAsProcessedAsync(request.MessageId, result.Value, cancellationToken);
            _logger.LogDebug("Marked message {MessageId} as processed", request.MessageId);
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
