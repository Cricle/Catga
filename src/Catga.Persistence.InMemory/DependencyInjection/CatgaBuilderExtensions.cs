using Catga.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.DependencyInjection;

/// <summary>Fluent API extensions for CatgaBuilder</summary>
public static class CatgaBuilderExtensions
{
    public static CatgaBuilder WithLogging(this CatgaBuilder builder, bool enabled = true)
        => builder.Configure(options => options.EnableLogging = enabled);

    public static CatgaBuilder UseProductionDefaults(this CatgaBuilder builder)
        => builder.WithLogging(true);

    public static CatgaBuilder UseDevelopmentDefaults(this CatgaBuilder builder)
        => builder.WithLogging(true);
}

