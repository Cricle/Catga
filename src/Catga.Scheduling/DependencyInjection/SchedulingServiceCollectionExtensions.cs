using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.Scheduling.DependencyInjection;

/// <summary>
/// DI extensions for Catga Scheduling
/// </summary>
public static class SchedulingServiceCollectionExtensions
{
    /// <summary>
    /// Add in-memory message scheduler
    /// </summary>
    public static IServiceCollection AddMemoryScheduler(this IServiceCollection services)
    {
        services.TryAddSingleton<IMessageScheduler, MemoryMessageScheduler>();
        return services;
    }
}

