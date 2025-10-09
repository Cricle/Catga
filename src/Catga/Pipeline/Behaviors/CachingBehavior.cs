using Catga.Caching;
using Catga.Messages;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Pipeline behavior that caches responses for cacheable requests
/// </summary>
public sealed class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, ICacheable
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<CachingBehavior<TRequest, TResponse>> _logger;

    public CachingBehavior(
        IDistributedCache cache,
        ILogger<CachingBehavior<TRequest, TResponse>> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var cacheKey = request.GetCacheKey();

        // Try get from cache
        var cached = await _cache.GetAsync<TResponse>(cacheKey, cancellationToken);
        if (cached != null)
        {
            _logger.LogDebug(
                "Cache hit for key: {CacheKey}",
                cacheKey);
            return CatgaResult<TResponse>.Success(cached);
        }

        _logger.LogDebug(
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

            _logger.LogDebug(
                "Cached response for key: {CacheKey}, expiration: {Expiration}",
                cacheKey,
                request.CacheExpiration);
        }

        return result;
    }
}

