using Catga.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.DependencyInjection;

/// <summary>
/// Fluent API extensions for CatgaBuilder
/// Provides a more intuitive and chainable configuration experience
/// </summary>
public static class CatgaBuilderExtensions
{
    /// <summary>
    /// Configure Catga options with fluent API
    /// </summary>
    public static CatgaBuilder Configure(this CatgaBuilder builder, Action<CatgaOptions> configure)
    {
        // Access options from builder's internal field
        var optionsField = builder.GetType().GetField("_options",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (optionsField != null)
        {
            var options = (CatgaOptions?)optionsField.GetValue(builder);
            if (options != null)
            {
                configure(options);
            }
        }

        return builder;
    }

    /// <summary>
    /// Enable logging with default configuration
    /// </summary>
    public static CatgaBuilder WithLogging(this CatgaBuilder builder, bool enabled = true)
    {
        return builder.Configure(options => options.EnableLogging = enabled);
    }

    /// <summary>
    /// Add all recommended production settings
    /// </summary>
    public static CatgaBuilder UseProductionDefaults(this CatgaBuilder builder)
    {
        return builder
            .WithLogging(true);
    }

    /// <summary>
    /// Add development-friendly settings (more permissive)
    /// </summary>
    public static CatgaBuilder UseDevelopmentDefaults(this CatgaBuilder builder)
    {
        return builder
            .WithLogging(true);
    }
}

