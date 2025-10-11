using System.Diagnostics.CodeAnalysis;
using Catga.Caching;
using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Pipeline behavior that caches responses for cacheable requests
/// </summary>
public sealed class CachingBehavior<TRequest, TResponse> : BaseBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, ICacheable
{
    private readonly IDistributedCache _cache;

    public CachingBehavior(
        IDistributedCache cache,
        ILogger<CachingBehavior<TRequest, TResponse>> logger)
        : base(logger)
    {
        _cache = cache;
    }

    [RequiresDynamicCode("Cache serialization may require runtime code generation")]
    [RequiresUnreferencedCode("Cache serialization may require types that cannot be statically analyzed")]
    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var cacheKey = request.GetCacheKey();

        // Try get from cache
        var cached = await _cache.GetAsync<TResponse>(cacheKey, cancellationToken);
        if (cached != null)
        {
            Logger.LogDebug(
                "Cache hit for key: {CacheKey}",
                cacheKey);
            return CatgaResult<TResponse>.Success(cached);
        }

        Logger.LogDebug(
            "Cache miss for key: {CacheKey}",
            cacheKey);

        // Execute handler
        var result = await next();

        // Cache the response if successful
        if (result.IsSuccess && result.Value != null)
        {
            await _cache.SetAsync(
                cacheKey,
                result.Value,
                request.CacheExpiration,
                cancellationToken);

            Logger.LogDebug(
                "Cached response for key: {CacheKey}, expiration: {Expiration}",
                cacheKey,
                request.CacheExpiration);
        }

        return result;
    }
}

