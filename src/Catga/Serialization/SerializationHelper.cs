using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Catga.Serialization;

namespace Catga;

/// <summary>Serialization helper - requires explicit serializer for type safety and AOT compatibility</summary>
public static class SerializationHelper
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>Serialize object using provided serializer (required)</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [UnconditionalSuppressMessage("Trimming", "IL2091", Justification = "Callers are responsible for ensuring T has proper DynamicallyAccessedMembers annotations.")]
    public static string Serialize<T>(T obj, IMessageSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        var bytes = serializer.Serialize(obj);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>Serialize to JSON (for debug/dev only - not AOT compatible)</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed. Use MemoryPack serializer for AOT or provide JsonSerializerContext.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("JSON serialization may require runtime code generation. Use MemoryPack serializer for AOT or use source-generated JsonSerializerContext.")]
    public static string SerializeJson<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] T>(T obj, JsonSerializerOptions? options = null)
        => JsonSerializer.Serialize(obj, options ?? DefaultJsonOptions);

    /// <summary>Deserialize object using provided serializer (required)</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [UnconditionalSuppressMessage("Trimming", "IL2091", Justification = "Callers are responsible for ensuring T has proper DynamicallyAccessedMembers annotations.")]
    public static T? Deserialize<T>(string data, IMessageSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        var bytes = Convert.FromBase64String(data);
        return serializer.Deserialize<T>(bytes);
    }

    /// <summary>Deserialize from JSON (for debug/dev only - not AOT compatible)</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed. Use MemoryPack serializer for AOT or provide JsonSerializerContext.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("JSON serialization may require runtime code generation. Use MemoryPack serializer for AOT or use source-generated JsonSerializerContext.")]
    public static T? DeserializeJson<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] T>(string data, JsonSerializerOptions? options = null)
        => JsonSerializer.Deserialize<T>(data, options ?? DefaultJsonOptions);

    /// <summary>Try deserialize using provided serializer (required)</summary>
    public static bool TryDeserialize<T>(string data, out T? result, IMessageSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        try
        {
            result = Deserialize<T>(data, serializer);
            return result != null;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    /// <summary>Try deserialize from JSON (for debug/dev only - not AOT compatible)</summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "InMemory store helpers are for development/testing. Use provided serializer or Redis for production AOT.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "InMemory store helpers are for development/testing. Use provided serializer or Redis for production AOT.")]
    public static bool TryDeserializeJson<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] T>(string data, out T? result, JsonSerializerOptions? options = null)
    {
        try
        {
            result = DeserializeJson<T>(data, options);
            return result != null;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    /// <summary>Get default JSON options (for advanced scenarios only)</summary>
    public static JsonSerializerOptions GetDefaultJsonOptions() => DefaultJsonOptions;
}
