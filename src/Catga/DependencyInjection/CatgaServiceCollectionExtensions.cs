using Catga;
using Catga.Configuration;
using Catga.DependencyInjection;
using Catga.DistributedId;
using Catga.EventSourcing;
using Catga.Observability;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Catga.Abstractions;
using Catga.Core;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods for configuring Catga services</summary>
public static class CatgaServiceCollectionExtensions
{
    /// <summary>Adds Catga mediator and core services to the service collection</summary>
    public static CatgaServiceBuilder AddCatga(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new CatgaOptions();
        services.TryAddSingleton(options);
        services.TryAddSingleton<ICatgaMediator, CatgaMediator>();
        services.TryAddSingleton<IDistributedIdGenerator>(sp => new SnowflakeIdGenerator(GetWorkerIdFromEnvironmentOrRandom("CATGA_WORKER_ID")));

        if (options.EnableTracing)
            ObservabilityHooks.Enable();

        services.TryAddSingleton<IEventTypeRegistry, DefaultEventTypeRegistry>();

        Catga.Generated.GeneratedBootstrapRegistry.Apply(services);
        var conv = Catga.Generated.GeneratedBootstrapRegistry.EndpointConvention;
        if (conv is not null && options.EndpointNamingConvention is null)
            options.EndpointNamingConvention = conv;

        return new CatgaServiceBuilder(services, options);
    }

    private static int GetWorkerIdFromEnvironmentOrRandom(string envVarName)
    {
        var envValue = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrEmpty(envValue) && int.TryParse(envValue, out var workerId))
        {
            if (workerId >= 0 && workerId <= 255)
                return workerId;
        }
        // Random WorkerId for backward compatibility (not recommended for production clusters)
        return Random.Shared.Next(0, 256);
    }

    /// <summary>Adds Catga mediator and core services with configuration</summary>
    public static CatgaServiceBuilder AddCatga(this IServiceCollection services, Action<CatgaOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new CatgaOptions();
        configure(options);
        services.TryAddSingleton(options);
        services.TryAddSingleton<ICatgaMediator, CatgaMediator>();
        services.TryAddSingleton<IDistributedIdGenerator>(sp => new SnowflakeIdGenerator(GetWorkerIdFromEnvironmentOrRandom("CATGA_WORKER_ID")));

        if (options.EnableTracing)
            ObservabilityHooks.Enable();

        services.TryAddSingleton<IEventTypeRegistry, DefaultEventTypeRegistry>();

        Catga.Generated.GeneratedBootstrapRegistry.Apply(services);
        var conv = Catga.Generated.GeneratedBootstrapRegistry.EndpointConvention;
        if (conv is not null && options.EndpointNamingConvention is null)
            options.EndpointNamingConvention = conv;

        return new CatgaServiceBuilder(services, options);
    }
}
