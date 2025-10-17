using System.Diagnostics.CodeAnalysis;
using Catga.Caching;
using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>Caching behavior</summary>
public sealed class CachingBehavior<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse> : BaseBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>, ICacheable
{
    private readonly IDistributedCache _cache;

    public CachingBehavior(IDistributedCache cache, ILogger<CachingBehavior<TRequest, TResponse>> logger) : base(logger)
        => _cache = cache;

    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var cacheKey = request.GetCacheKey();
        var cached = await _cache.GetAsync<TResponse>(cacheKey, cancellationToken);
        if (cached != null)
        {
            Logger.LogDebug("Cache hit for key: {CacheKey}", cacheKey);
            return CatgaResult<TResponse>.Success(cached);
        }

        Logger.LogDebug("Cache miss for key: {CacheKey}", cacheKey);
        var result = await next();

        if (result.IsSuccess && result.Value != null)
        {
            await _cache.SetAsync(cacheKey, result.Value, request.CacheExpiration, cancellationToken);
            Logger.LogDebug("Cached response for key: {CacheKey}, expiration: {Expiration}", cacheKey, request.CacheExpiration);
        }
        return result;
    }
}

