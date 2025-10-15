using Catga.Debugging;
using Catga.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.DependencyInjection;

/// <summary>
/// Debug extensions - enable native debugging with one line
/// </summary>
public static class DebugExtensions
{
    /// <summary>
    /// Enable Catga native debugging - see message flows in real-time
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddCatga()
    ///     .WithDebug();  // ← One line!
    /// </code>
    /// </example>
    public static CatgaServiceBuilder WithDebug(this CatgaServiceBuilder builder)
    {
        var options = new DebugOptions { EnableDebug = true };
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<MessageFlowTracker>();
        builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(DebugPipelineBehavior<,>));

        return builder;
    }

    /// <summary>
    /// Enable Catga native debugging with custom options
    /// </summary>
    public static CatgaServiceBuilder WithDebug(
        this CatgaServiceBuilder builder,
        Action<DebugOptions> configure)
    {
        var options = new DebugOptions { EnableDebug = true };
        configure(options);

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<MessageFlowTracker>();
        builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(DebugPipelineBehavior<,>));

        // Add debug API endpoints if enabled
        if (options.EnableApiEndpoints)
        {
            // Will be mapped in app configuration
        }

        return builder;
    }
}

