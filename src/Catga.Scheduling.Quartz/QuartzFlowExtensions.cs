using Catga.DependencyInjection;
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
    /// Adds Quartz.NET-based flow scheduling to Catga.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddCatga()
    ///     .UseQuartzScheduling();
    /// </code>
    /// </example>
    public static CatgaServiceBuilder UseQuartzScheduling(
        this CatgaServiceBuilder builder,
        Action<QuartzFlowOptions>? configure = null)
    {
        var options = new QuartzFlowOptions();
        configure?.Invoke(options);

        // Add Quartz with default configuration if not already added
        builder.Services.AddQuartz(q =>
        {
            // Register the FlowResumeJob
            q.AddJob<FlowResumeJob>(j => j
                .WithIdentity("FlowResumeJob", "catga-flow")
                .StoreDurably());
        });

        // Add Quartz hosted service if configured
        if (options.UseHostedService)
        {
            builder.Services.AddQuartzHostedService(q =>
            {
                q.WaitForJobsToComplete = options.WaitForJobsToComplete;
            });
        }

        // Register IFlowScheduler
        builder.Services.AddSingleton<IFlowScheduler, QuartzFlowScheduler>();

        return builder;
    }

    /// <summary>
    /// Adds Quartz.NET-based flow scheduling with custom Quartz configuration.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddCatga()
    ///     .UseQuartzScheduling(q => q.UsePersistentStore(...));
    /// </code>
    /// </example>
    public static CatgaServiceBuilder UseQuartzScheduling(
        this CatgaServiceBuilder builder,
        Action<IServiceCollectionQuartzConfigurator> configureQuartz,
        Action<QuartzFlowOptions>? configureOptions = null)
    {
        var options = new QuartzFlowOptions();
        configureOptions?.Invoke(options);

        builder.Services.AddQuartz(q =>
        {
            // Register the FlowResumeJob
            q.AddJob<FlowResumeJob>(j => j
                .WithIdentity("FlowResumeJob", "catga-flow")
                .StoreDurably());

            // Apply custom configuration
            configureQuartz(q);
        });

        if (options.UseHostedService)
        {
            builder.Services.AddQuartzHostedService(q =>
            {
                q.WaitForJobsToComplete = options.WaitForJobsToComplete;
            });
        }

        builder.Services.AddSingleton<IFlowScheduler, QuartzFlowScheduler>();

        return builder;
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
