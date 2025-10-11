using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.Threading.DependencyInjection;

/// <summary>
/// DI extensions for Catga.Threading
/// Thread pools are registered as singletons (one instance per service provider)
/// </summary>
public static class ThreadingServiceCollectionExtensions
{
    /// <summary>
    /// Add Work-Stealing Thread Pool (best for CPU-bound parallel tasks)
    /// Registered as singleton - one pool instance per application
    /// </summary>
    public static IServiceCollection AddWorkStealingThreadPool(
        this IServiceCollection services,
        Action<ThreadPoolOptions>? configure = null)
    {
        var options = new ThreadPoolOptions();
        configure?.Invoke(options);

        // Register as singleton - instance-based, not static
        services.TryAddSingleton<IThreadPool>(sp => new WorkStealingThreadPool(options));
        services.TryAddSingleton<WorkStealingThreadPool>(sp => (WorkStealingThreadPool)sp.GetRequiredService<IThreadPool>());
        
        return services;
    }

    /// <summary>
    /// Add IO Thread Pool (best for IO-bound async operations)
    /// Registered as singleton - one pool instance per application
    /// </summary>
    public static IServiceCollection AddIOThreadPool(
        this IServiceCollection services,
        int maxConcurrency = 0)
    {
        // Register as singleton - instance-based, not static
        services.TryAddSingleton(sp => new IOThreadPool(maxConcurrency));
        
        return services;
    }

    /// <summary>
    /// Add both CPU and IO thread pools
    /// Both are instance-based singletons injected via DI
    /// </summary>
    public static IServiceCollection AddCatgaThreading(
        this IServiceCollection services,
        Action<ThreadPoolOptions>? configureCpu = null,
        int ioMaxConcurrency = 0)
    {
        services.AddWorkStealingThreadPool(configureCpu);
        services.AddIOThreadPool(ioMaxConcurrency);
        return services;
    }
}

