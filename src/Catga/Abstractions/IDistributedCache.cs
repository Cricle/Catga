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
    public ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set value in cache
    /// </summary>
    public ValueTask SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove value from cache
    /// </summary>
    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if key exists in cache
    /// </summary>
    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh expiration time
    /// </summary>
    public ValueTask RefreshAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default);
}

/// <summary>
/// Marker interface for cacheable requests
/// </summary>
public interface ICacheable
{
    /// <summary>
    /// Get cache key for this request
    /// </summary>
    public string GetCacheKey();

    /// <summary>
    /// Cache expiration duration
    /// </summary>
    public TimeSpan CacheExpiration => TimeSpan.FromMinutes(5);
}

