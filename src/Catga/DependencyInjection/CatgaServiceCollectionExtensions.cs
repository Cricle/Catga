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
        services.TryAddScoped<ICatgaMediator, CatgaMediator>();

        // Register default SnowflakeIdGenerator with WorkerId from environment or random
        // Users can override this by calling .UseWorkerId(n) or .UseWorkerIdFromEnvironment()
        services.TryAddSingleton<IDistributedIdGenerator>(sp =>
        {
            var workerId = GetWorkerIdFromEnvironmentOrRandom("CATGA_WORKER_ID");
            return new SnowflakeIdGenerator(workerId);
        });

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

