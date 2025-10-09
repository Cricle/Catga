using Microsoft.Extensions.DependencyInjection;

namespace Catga.Saga;

/// <summary>
/// Extension methods for adding saga support to the service collection
/// </summary>
public static class SagaServiceCollectionExtensions
{
    /// <summary>
    /// Add saga executor
    /// </summary>
    public static IServiceCollection AddSagaExecutor(
        this IServiceCollection services)
    {
        services.AddSingleton<SagaExecutor>();
        return services;
    }
}

