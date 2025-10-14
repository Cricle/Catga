using Catga.Serialization;
using Catga.Serialization.MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.DependencyInjection;

/// <summary>
/// MemoryPack serializer extensions for easy configuration (100% AOT compatible)
/// </summary>
public static class MemoryPackSerializerExtensions
{
    /// <summary>
    /// Use MemoryPack serializer (recommended for Native AOT)
    /// </summary>
    /// <remarks>
    /// MemoryPack provides:
    /// - ✅ 100% AOT compatible (no reflection)
    /// - ✅ 5x faster than JSON
    /// - ✅ 40% smaller payload
    /// - ✅ Zero-copy deserialization
    ///
    /// All message types must be annotated with [MemoryPackable]:
    /// <code>
    /// [MemoryPackable]
    /// public partial record MyCommand(string Data) : IRequest&lt;MyResult&gt;;
    /// </code>
    /// </remarks>
    public static IServiceCollection UseMemoryPackSerializer(this IServiceCollection services)
    {
        services.TryAddSingleton<IMessageSerializer, MemoryPackMessageSerializer>();
        return services;
    }

    /// <summary>
    /// Use MemoryPack serializer with fluent builder
    /// </summary>
    public static CatgaServiceBuilder UseMemoryPack(this CatgaServiceBuilder builder)
    {
        builder.Services.UseMemoryPackSerializer();
        return builder;
    }
}

