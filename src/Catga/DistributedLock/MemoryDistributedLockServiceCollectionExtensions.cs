using Microsoft.Extensions.DependencyInjection;

namespace Catga.DistributedLock;

/// <summary>
/// Extension methods for adding memory distributed lock to the service collection
/// </summary>
public static class MemoryDistributedLockServiceCollectionExtensions
{
    /// <summary>
    /// Add in-memory distributed lock (for single-instance scenarios or testing)
    /// </summary>
    public static IServiceCollection AddMemoryDistributedLock(
        this IServiceCollection services)
    {
        services.AddSingleton<IDistributedLock, MemoryDistributedLock>();
        return services;
    }
}

