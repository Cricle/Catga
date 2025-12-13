using Catga;
using Catga.Configuration;
using Catga.DependencyInjection;
using Catga.DistributedId;
using Catga.EventSourcing;
using Catga.Observability;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Diagnostics;
using Catga.Abstractions;
using Catga.Core;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring Catga services
/// </summary>
public static class CatgaServiceCollectionExtensions
{
    // ========== Core Service Registration ==========

    /// <summary>
    /// Adds Catga mediator and core services to the service collection
    /// </summary>
    public static CatgaServiceBuilder AddCatga(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var sw = Stopwatch.StartNew();

        // Register Catga options
        var options = new CatgaOptions();
        services.TryAddSingleton(options);

        // Register core services (Singleton for performance, uses root IServiceProvider)
        services.TryAddSingleton<ICatgaMediator, CatgaMediator>();

        // Register default SnowflakeIdGenerator with WorkerId from environment or random
        // Users can override this by calling .UseWorkerId(n) or .UseWorkerIdFromEnvironment()
        services.TryAddSingleton<IDistributedIdGenerator>(sp => new SnowflakeIdGenerator(GetWorkerIdFromEnvironmentOrRandom("CATGA_WORKER_ID")));

        // Enable observability hooks only if tracing is enabled (default: true)
        if (options.EnableTracing)
            ObservabilityHooks.Enable();

        services.TryAddSingleton<IEventTypeRegistry, DefaultEventTypeRegistry>();

        Catga.Generated.GeneratedBootstrapRegistry.Apply(services);
        var conv = Catga.Generated.GeneratedBootstrapRegistry.EndpointConvention;
        if (conv is not null && options.EndpointNamingConvention is null)
            options.EndpointNamingConvention = conv;
        var builder = new CatgaServiceBuilder(services, options);

        sw.Stop();
        var totalMilliseconds = sw.Elapsed.TotalMilliseconds;
        var tag = new KeyValuePair<string, object?>("component", "DI.Core");
        try
        {
            CatgaDiagnostics.DIRegistrationsCompleted.Add(1, tag);
            return builder;
        }
        catch
        {
            CatgaDiagnostics.DIRegistrationsFailed.Add(1, tag);
            throw;
        }
        finally
        {
            CatgaDiagnostics.DIRegistrationDuration.Record(totalMilliseconds, tag);
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

        var sw = Stopwatch.StartNew();

        // Create and configure options BEFORE registering services
        var options = new CatgaOptions();
        configure(options);
        services.TryAddSingleton(options);

        // Register core services (Singleton for performance, uses root IServiceProvider)
        services.TryAddSingleton<ICatgaMediator, CatgaMediator>();

        // Register default SnowflakeIdGenerator
        services.TryAddSingleton<IDistributedIdGenerator>(sp => new SnowflakeIdGenerator(GetWorkerIdFromEnvironmentOrRandom("CATGA_WORKER_ID")));

        // Enable observability hooks only if tracing is enabled
        if (options.EnableTracing)
            ObservabilityHooks.Enable();

        services.TryAddSingleton<IEventTypeRegistry, DefaultEventTypeRegistry>();

        Catga.Generated.GeneratedBootstrapRegistry.Apply(services);
        var conv = Catga.Generated.GeneratedBootstrapRegistry.EndpointConvention;
        if (conv is not null && options.EndpointNamingConvention is null)
            options.EndpointNamingConvention = conv;
        var builder = new CatgaServiceBuilder(services, options);

        sw.Stop();
        var totalMilliseconds = sw.Elapsed.TotalMilliseconds;
        var tag = new KeyValuePair<string, object?>("component", "DI.Core");
        try
        {
            CatgaDiagnostics.DIRegistrationsCompleted.Add(1, tag);
            return builder;
        }
        catch
        {
            CatgaDiagnostics.DIRegistrationsFailed.Add(1, tag);
            throw;
        }
        finally
        {
            CatgaDiagnostics.DIRegistrationDuration.Record(totalMilliseconds, tag);
        }
    }
}
