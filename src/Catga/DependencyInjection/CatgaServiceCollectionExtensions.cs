using System;
using Catga;
using Catga.Configuration;
using Catga.Generated;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Catga.Observability;
using Catga.DependencyInjection;
using Catga.Resilience;
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
        var sw = Stopwatch.StartNew();
        var tags = new TagList { { "component", "DI.Core" } };

        // Register Catga options
        var options = new CatgaOptions();
        TrySetGeneratedEndpointNaming(options);
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

        var builder = new CatgaServiceBuilder(services, options);

        try
        {
            TryInvokeGeneratedRegistrations(services, options);
            sw.Stop();
            CatgaDiagnostics.DIRegistrationsCompleted.Add(1, tags);
            CatgaDiagnostics.DIRegistrationDuration.Record(sw.Elapsed.TotalMilliseconds, tags);
            return builder;
        }
        catch
        {
            sw.Stop();
            CatgaDiagnostics.DIRegistrationsFailed.Add(1, tags);
            CatgaDiagnostics.DIRegistrationDuration.Record(sw.Elapsed.TotalMilliseconds, tags);
            throw;
        }
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

    private static void TryInvokeGeneratedRegistrations(IServiceCollection services, CatgaOptions options)
    {
        // Reflection-free bootstrap: apply registrations and endpoint naming
        GeneratedBootstrapRegistry.Apply(services, options);
    }

    private static void TrySetGeneratedEndpointNaming(CatgaOptions options)
    {
        // Intentionally left blank: naming is applied via GeneratedBootstrapRegistry.Apply
    }
}
