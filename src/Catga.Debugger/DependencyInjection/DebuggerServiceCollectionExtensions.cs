using Catga.Debugger.Core;
using Catga.Debugger.Models;
using Catga.Debugger.Pipeline;
using Catga.Debugger.Replay;
using Catga.Debugger.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        // Register pipeline behavior for event capture
        services.AddSingleton(typeof(ReplayableEventCapturer<,>));

        return services;
    }

    /// <summary>Add Catga debugger with production-optimized settings</summary>
    public static IServiceCollection AddCatgaDebuggerForProduction(
        this IServiceCollection services)
    {
        return services.AddCatgaDebugger(options =>
        {
            options.Mode = DebuggerMode.ProductionOptimized;
            options.SamplingRate = 0.001; // 0.1%
            options.EnableAdaptiveSampling = true;
            options.UseRingBuffer = true;
            options.MaxMemoryMB = 50;
            options.EnableZeroCopy = true;
            options.EnableObjectPooling = true;
            options.TrackStateSnapshots = false; // Disable snapshots in production
            options.CaptureMemoryState = false;
            options.CaptureCallStacks = false;
            options.AutoDisableAfter = TimeSpan.FromMinutes(30);
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

