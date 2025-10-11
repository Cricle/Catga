using System.Diagnostics.CodeAnalysis;

namespace Catga.Caching;

/// <summary>
/// Distributed cache abstraction
/// </summary>
public interface IDistributedCache
{
    /// <summary>
    /// Get value from cache
    /// </summary>
    [RequiresUnreferencedCode("Cache serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Cache serialization may require runtime code generation")]
    ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set value in cache
    /// </summary>
    [RequiresUnreferencedCode("Cache serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Cache serialization may require runtime code generation")]
    ValueTask SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove value from cache
    /// </summary>
    ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if key exists in cache
    /// </summary>
    ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh expiration time
    /// </summary>
    ValueTask RefreshAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default);
}

/// <summary>
/// Marker interface for cacheable requests
/// </summary>
public interface ICacheable
{
    /// <summary>
    /// Get cache key for this request
    /// </summary>
    string GetCacheKey();

    /// <summary>
    /// Cache expiration duration
    /// </summary>
    TimeSpan CacheExpiration => TimeSpan.FromMinutes(5);
}

