using Catga;
using Catga.Configuration;
using Catga.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring Catga services
/// </summary>
public static class CatgaServiceCollectionExtensions
{
    /// <summary>
    /// Adds Catga mediator and core services to the service collection
    /// </summary>
    public static IServiceCollection AddCatga(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register Catga options
        services.TryAddSingleton<CatgaOptions>();

        // Register core services
        services.TryAddScoped<ICatgaMediator, Catga.Mediator.CatgaMediator>();
        services.TryAddSingleton<Catga.Abstractions.IDistributedIdGenerator, SnowflakeIdGenerator>();

        return services;
    }

    /// <summary>
    /// Adds Catga mediator and core services with configuration
    /// </summary>
    public static IServiceCollection AddCatga(this IServiceCollection services, Action<CatgaOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddCatga();

        // Configure options
        services.Configure(configure);

        return services;
    }
}

