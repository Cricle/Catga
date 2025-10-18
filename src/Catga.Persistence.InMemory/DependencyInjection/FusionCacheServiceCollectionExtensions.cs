using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZiggyCreatures.Caching.Fusion;

namespace Catga.Persistence.DependencyInjection;

/// <summary>
/// FusionCache configuration extensions for Catga InMemory persistence
/// </summary>
public static class FusionCacheServiceCollectionExtensions
{
    /// <summary>
    /// Add FusionCache for Catga InMemory stores with optimized settings
    /// </summary>
    public static IServiceCollection AddCatgaFusionCache(
        this IServiceCollection services,
        Action<FusionCacheOptions>? configure = null)
    {
        // Add Memory Cache if not already registered
        services.TryAddSingleton<IMemoryCache>(sp =>
        {
            var options = new MemoryDistributedCacheOptions
            {
                SizeLimit = 1024 * 1024 * 100 // 100MB default limit
            };
            return new MemoryCache(options);
        });

        // Add FusionCache with optimized settings
        services.AddFusionCache()
            .WithOptions(options =>
            {
                // Optimized defaults for Catga InMemory (no fail-safe needed)
                options.DefaultEntryOptions = new FusionCacheEntryOptions
                {
                    Duration = TimeSpan.FromHours(24),
                    Priority = CacheItemPriority.Normal,
                    
                    // Disable fail-safe for in-memory (no distributed cache fallback)
                    IsFailSafeEnabled = false,
                    
                    // No factory timeouts for in-memory operations
                    FactoryHardTimeout = Timeout.InfiniteTimeSpan,
                    FactorySoftTimeout = Timeout.InfiniteTimeSpan
                };

                // Allow custom configuration
                configure?.Invoke(options);
            });

        return services;
    }

    /// <summary>
    /// Add FusionCache with custom memory cache options
    /// </summary>
    public static IServiceCollection AddCatgaFusionCache(
        this IServiceCollection services,
        Action<MemoryDistributedCacheOptions> configureMemoryCache,
        Action<FusionCacheOptions>? configureFusionCache = null)
    {
        // Add Memory Cache with custom options
        services.TryAddSingleton<IMemoryCache>(sp =>
        {
            var options = new MemoryDistributedCacheOptions();
            configureMemoryCache(options);
            return new MemoryCache(options);
        });

        // Add FusionCache
        return services.AddCatgaFusionCache(configureFusionCache);
    }
}

