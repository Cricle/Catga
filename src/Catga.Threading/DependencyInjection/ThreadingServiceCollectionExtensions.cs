using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.Threading.DependencyInjection;

/// <summary>
/// DI extensions for Catga.Threading
/// </summary>
public static class ThreadingServiceCollectionExtensions
{
    /// <summary>
    /// Add Work-Stealing Thread Pool (best for CPU-bound parallel tasks)
    /// </summary>
    public static IServiceCollection AddWorkStealingThreadPool(
        this IServiceCollection services,
        Action<ThreadPoolOptions>? configure = null)
    {
        var options = new ThreadPoolOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<IThreadPool>(_ => new WorkStealingThreadPool(options));
        return services;
    }

    /// <summary>
    /// Add IO Thread Pool (best for IO-bound async operations)
    /// </summary>
    public static IServiceCollection AddIOThreadPool(
        this IServiceCollection services,
        int maxConcurrency = 0)
    {
        services.TryAddSingleton(_ => new IOThreadPool(maxConcurrency));
        return services;
    }

    /// <summary>
    /// Add both CPU and IO thread pools
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

