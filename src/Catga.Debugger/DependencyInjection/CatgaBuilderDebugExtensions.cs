using Catga.Debugger.Models;
using Catga.DependencyInjection;

namespace Catga.Debugger.DependencyInjection;

/// <summary>
/// Simplified debug extensions for CatgaServiceBuilder
/// </summary>
public static class CatgaBuilderDebugExtensions
{
    /// <summary>
    /// Enable Catga debugging - automatically detects environment and applies appropriate settings
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services
    ///     .AddCatga()
    ///     .UseMemoryPack()
    ///     .WithDebug()  // ← One line!
    ///     .ForDevelopment();
    /// </code>
    /// </example>
    /// <remarks>
    /// - Development: 100% sampling, full features
    /// - Production: 0.1% sampling, minimal overhead
    /// </remarks>
    public static CatgaServiceBuilder WithDebug(
        this CatgaServiceBuilder builder)
    {
        // Auto-detect environment
        var isDevelopment = IsDefaultDevelopment();

        if (isDevelopment)
        {
            builder.Services.AddCatgaDebuggerForDevelopment();
        }
        else
        {
            builder.Services.AddCatgaDebuggerForProduction();
        }

        return builder;
    }

    /// <summary>
    /// Enable Catga debugging with custom configuration
    /// </summary>
    public static CatgaServiceBuilder WithDebug(
        this CatgaServiceBuilder builder,
        Action<ReplayOptions> configure)
    {
        builder.Services.AddCatgaDebugger(configure);
        return builder;
    }

    private static bool IsDefaultDevelopment()
    {
        // Try to detect from environment variable
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                       ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");

        return environment?.Equals("Development", StringComparison.OrdinalIgnoreCase) ?? false;
    }
}

