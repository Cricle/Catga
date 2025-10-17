using Catga.Debugger.Breakpoints;
using Catga.Debugger.CallStack;
using Catga.Debugger.Core;
using Catga.Debugger.HealthChecks;
using Catga.Debugger.Models;
using Catga.Debugger.Pipeline;
using Catga.Debugger.Profiling;
using Catga.Debugger.Replay;
using Catga.Debugger.Storage;
using Catga.Debugger.Watch;
using Catga.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Catga.Debugger.DependencyInjection;

/// <summary>Service collection extensions for Catga.Debugger</summary>
public static class DebuggerServiceCollectionExtensions
{
    /// <summary>Add Catga time-travel debugger</summary>
    public static IServiceCollection AddCatgaDebugger(
        this IServiceCollection services,
        Action<ReplayOptions>? configureOptions = null)
    {
        // Configure options
        var options = new ReplayOptions();
        configureOptions?.Invoke(options);
        services.AddSingleton(options);

        // Core services
        services.AddSingleton<AdaptiveSampler>();
        services.TryAddSingleton<IEventStore, InMemoryEventStore>();
        services.AddSingleton<IReplayEngine, TimeTravelReplayEngine>();
        services.AddSingleton<StateReconstructor>();
        services.AddSingleton<ReplaySessionManager>();

        // Register pipeline behavior for event capture (only if replay enabled)
        if (options.EnableReplay)
        {
            services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(ReplayableEventCapturer<,>));
        }

        // Advanced debugging features (conditionally registered)
        RegisterDebugFeatures(services, options);

        return services;
    }

    private static void RegisterDebugFeatures(IServiceCollection services, ReplayOptions options)
    {
        // Breakpoint system
        services.AddSingleton(sp => new BreakpointManager(
            sp.GetRequiredService<ILogger<BreakpointManager>>(),
            enabled: options.Mode == DebuggerMode.Development
        ));

        if (options.Mode == DebuggerMode.Development)
        {
            services.AddSingleton(typeof(BreakpointBehavior<,>));
        }

        // Watch system
        services.AddSingleton(sp => new WatchManager(
            sp.GetRequiredService<ILogger<WatchManager>>(),
            enabled: options.Mode == DebuggerMode.Development
        ));

        // Call stack tracking
        services.AddSingleton(sp => new CallStackTracker(
            enabled: options.CaptureCallStacks,
            captureVariables: options.CaptureVariables
        ));

        if (options.CaptureCallStacks)
        {
            services.AddSingleton(typeof(CallStackBehavior<,>));
        }

        // Performance profiling
        services.AddSingleton<FlameGraphBuilder>();
        services.AddSingleton<PerformanceAnalyzer>();

        // Note: Health check registration is done externally to avoid conflicts with Aspire
        // See: examples/OrderSystem.ServiceDefaults/Extensions.cs
    }

    /// <summary>Add Catga debugger with production-optimized settings</summary>
    /// <remarks>
    /// Production mode focuses on metrics and tracing only, disables expensive features.
    /// Safe for production use with minimal overhead.
    /// </remarks>
    public static IServiceCollection AddCatgaDebuggerForProduction(
        this IServiceCollection services)
    {
        return services.AddCatgaDebugger(options =>
        {
            options.Mode = DebuggerMode.ProductionOptimized;

            // Disable time-travel debugging (use only for development)
            options.EnableReplay = false;
            options.TrackStateSnapshots = false;
            options.CaptureVariables = false;
            options.CaptureCallStacks = false;
            options.CaptureMemoryState = false;

            // Enable lightweight monitoring only
            options.TrackMessageFlows = false; // Use OpenTelemetry traces instead
            options.TrackPerformance = false;  // Use OpenTelemetry metrics instead
            options.TrackExceptions = true;    // Keep exception tracking

            // Performance optimizations
            options.SamplingRate = 0.01; // 1% sampling for exceptions
            options.EnableAdaptiveSampling = true;
            options.UseRingBuffer = true;
            options.MaxMemoryMB = 50;
            options.EnableZeroCopy = true;
            options.EnableObjectPooling = true;

            // Auto-disable after period
            options.AutoDisableAfter = TimeSpan.FromHours(2);
        });
    }

    /// <summary>Add Catga debugger with development settings</summary>
    public static IServiceCollection AddCatgaDebuggerForDevelopment(
        this IServiceCollection services)
    {
        return services.AddCatgaDebugger(options =>
        {
            options.Mode = DebuggerMode.Development;
            options.SamplingRate = 1.0; // 100%
            options.EnableAdaptiveSampling = false;
            options.TrackMessageFlows = true;
            options.TrackPerformance = true;
            options.TrackStateSnapshots = true;
            options.TrackExceptions = true;
            options.CaptureVariables = true;
            options.CaptureCallStacks = true;
            options.CaptureMemoryState = true;
        });
    }
}

