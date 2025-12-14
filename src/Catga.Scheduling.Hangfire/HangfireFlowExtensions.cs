using Catga.DependencyInjection;
using Catga.Flow;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Scheduling.Hangfire;

/// <summary>
/// Extension methods for configuring Hangfire flow scheduling.
/// </summary>
public static class HangfireFlowExtensions
{
    /// <summary>
    /// Adds Hangfire-based flow scheduling to Catga.
    /// Requires Hangfire to be configured separately with storage.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddHangfire(config => config.UseInMemoryStorage());
    /// services.AddHangfireServer();
    /// services.AddCatga()
    ///     .UseHangfireScheduling();
    /// </code>
    /// </example>
    public static CatgaServiceBuilder UseHangfireScheduling(this CatgaServiceBuilder builder)
    {
        // Register the flow resume service for Hangfire to invoke
        builder.Services.AddScoped<FlowResumeService>();

        // Register IFlowScheduler
        builder.Services.AddSingleton<IFlowScheduler, HangfireFlowScheduler>();

        return builder;
    }

    /// <summary>
    /// Adds Hangfire-based flow scheduling with Hangfire configuration.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddCatga()
    ///     .UseHangfireScheduling(config => config.UseInMemoryStorage());
    /// </code>
    /// </example>
    public static CatgaServiceBuilder UseHangfireScheduling(
        this CatgaServiceBuilder builder,
        Action<IGlobalConfiguration> configureHangfire)
    {
        builder.Services.AddHangfire(configureHangfire);
        builder.Services.AddHangfireServer();

        // Register the flow resume service
        builder.Services.AddScoped<FlowResumeService>();

        // Register IFlowScheduler
        builder.Services.AddSingleton<IFlowScheduler, HangfireFlowScheduler>();

        return builder;
    }
}
