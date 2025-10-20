using Catga.Abstractions;
using Catga.Serialization.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;

namespace Catga.DependencyInjection;

/// <summary>
/// JSON serializer extensions for easy configuration
/// </summary>
public static class JsonSerializerExtensions
{
    /// <summary>
    /// Use JSON serializer with default options (uses reflection - not recommended for AOT)
    /// </summary>
    /// <remarks>
    /// ⚠️ Warning: Default JSON serializer uses reflection and is NOT AOT compatible.
    ///
    /// For Native AOT, use the overload that accepts JsonSerializerOptions with JsonSerializerContext:
    /// <code>
    /// [JsonSerializable(typeof(MyCommand))]
    /// [JsonSerializable(typeof(MyResult))]
    /// public partial class MyJsonContext : JsonSerializerContext { }
    ///
    /// services.UseJsonSerializer(new JsonSerializerOptions
    /// {
    ///     TypeInfoResolver = MyJsonContext.Default
    /// });
    /// </code>
    ///
    /// Or use MemoryPack for full AOT support:
    /// <code>
    /// services.UseMemoryPackSerializer();
    /// </code>
    /// </remarks>
    public static IServiceCollection UseJsonSerializer(this IServiceCollection services)
    {
        services.TryAddSingleton<IMessageSerializer, JsonMessageSerializer>();
        return services;
    }

    /// <summary>
    /// Use JSON serializer with custom options (for AOT, provide JsonSerializerContext)
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="options">JSON serializer options (for AOT, set TypeInfoResolver to your JsonSerializerContext)</param>
    /// <remarks>
    /// For Native AOT compatibility, create a JsonSerializerContext:
    /// <code>
    /// [JsonSerializable(typeof(MyCommand))]
    /// [JsonSerializable(typeof(MyResult))]
    /// public partial class MyJsonContext : JsonSerializerContext { }
    ///
    /// var options = new JsonSerializerOptions
    /// {
    ///     TypeInfoResolver = MyJsonContext.Default,
    ///     PropertyNameCaseInsensitive = true
    /// };
    ///
    /// services.UseJsonSerializer(options);
    /// </code>
    /// </remarks>
    public static IServiceCollection UseJsonSerializer(
        this IServiceCollection services,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        services.TryAddSingleton<IMessageSerializer>(sp => new JsonMessageSerializer(options));
        return services;
    }

    /// <summary>
    /// Use JSON serializer with configuration action
    /// </summary>
    public static IServiceCollection UseJsonSerializer(
        this IServiceCollection services,
        Action<JsonSerializerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
        configure(options);

        services.TryAddSingleton<IMessageSerializer>(sp => new JsonMessageSerializer(options));
        return services;
    }

    // Note: CatgaServiceBuilder fluent extensions removed due to circular dependency.
    // Use IServiceCollection extensions directly: services.UseJsonSerializer()
}

