using Catga.Flow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;

namespace Catga.Scheduling.Quartz;

/// <summary>
/// Extension methods for configuring Quartz.NET flow scheduling.
/// </summary>
public static class QuartzFlowExtensions
{
    /// <summary>
    /// Adds Quartz.NET-based flow scheduling to the service collection.
    /// </summary>
    public static IServiceCollection AddCatgaQuartzScheduler(
        this IServiceCollection services,
        Action<QuartzFlowOptions>? configure = null)
    {
        var options = new QuartzFlowOptions();
        configure?.Invoke(options);

        // Add Quartz with default configuration if not already added
        services.AddQuartz(q =>
        {
            q.UseMicrosoftDependencyInjectionJobFactory();

            // Register the FlowResumeJob
            q.AddJob<FlowResumeJob>(j => j
                .WithIdentity("FlowResumeJob", "catga-flow")
                .StoreDurably());
        });

        // Add Quartz hosted service if configured
        if (options.UseHostedService)
        {
            services.AddQuartzHostedService(q =>
            {
                q.WaitForJobsToComplete = options.WaitForJobsToComplete;
            });
        }

        // Register IFlowScheduler
        services.AddSingleton<IFlowScheduler, QuartzFlowScheduler>();

        return services;
    }

    /// <summary>
    /// Adds Quartz.NET-based flow scheduling with custom Quartz configuration.
    /// </summary>
    public static IServiceCollection AddCatgaQuartzScheduler(
        this IServiceCollection services,
        Action<IServiceCollectionQuartzConfigurator> configureQuartz,
        Action<QuartzFlowOptions>? configureOptions = null)
    {
        var options = new QuartzFlowOptions();
        configureOptions?.Invoke(options);

        services.AddQuartz(q =>
        {
            q.UseMicrosoftDependencyInjectionJobFactory();

            // Register the FlowResumeJob
            q.AddJob<FlowResumeJob>(j => j
                .WithIdentity("FlowResumeJob", "catga-flow")
                .StoreDurably());

            // Apply custom configuration
            configureQuartz(q);
        });

        if (options.UseHostedService)
        {
            services.AddQuartzHostedService(q =>
            {
                q.WaitForJobsToComplete = options.WaitForJobsToComplete;
            });
        }

        services.AddSingleton<IFlowScheduler, QuartzFlowScheduler>();

        return services;
    }
}

/// <summary>
/// Options for Quartz flow scheduling.
/// </summary>
public sealed class QuartzFlowOptions
{
    /// <summary>
    /// Whether to add the Quartz hosted service. Default: true.
    /// </summary>
    public bool UseHostedService { get; set; } = true;

    /// <summary>
    /// Whether to wait for jobs to complete on shutdown. Default: true.
    /// </summary>
    public bool WaitForJobsToComplete { get; set; } = true;
}
