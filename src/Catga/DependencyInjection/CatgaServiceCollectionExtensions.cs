using Catga;
using Catga.Configuration;
using Catga.DependencyInjection;
using Catga.DistributedId;
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
    public static CatgaServiceBuilder AddCatga(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register Catga options
        var options = new CatgaOptions();
        services.TryAddSingleton(options);

        // Register core services
        services.TryAddScoped<ICatgaMediator, Catga.Mediator.CatgaMediator>();
        services.TryAddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();

        return new CatgaServiceBuilder(services, options);
    }

    /// <summary>
    /// Adds Catga mediator and core services with configuration
    /// </summary>
    public static CatgaServiceBuilder AddCatga(this IServiceCollection services, Action<CatgaOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = services.AddCatga();
        configure(builder.Options);

        return builder;
    }
}

