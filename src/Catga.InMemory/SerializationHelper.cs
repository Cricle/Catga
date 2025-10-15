using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Catga.Serialization;

namespace Catga.Common;

/// <summary>Common serialization helper (AOT-friendly)</summary>
public static class SerializationHelper
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "InMemory store helpers are for development/testing. Use provided serializer or Redis for production AOT.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "InMemory store helpers are for development/testing. Use provided serializer or Redis for production AOT.")]
    public static string Serialize<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] T>(T obj, IMessageSerializer? serializer = null)
    {
        if (serializer != null)
        {
            var bytes = serializer.Serialize(obj);
            return Convert.ToBase64String(bytes);
        }
        return JsonSerializer.Serialize(obj, DefaultJsonOptions);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed. Use MemoryPack serializer for AOT or provide JsonSerializerContext.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("JSON serialization may require runtime code generation. Use MemoryPack serializer for AOT or use source-generated JsonSerializerContext.")]
    public static string SerializeJson<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] T>(T obj, JsonSerializerOptions? options = null)
        => JsonSerializer.Serialize(obj, options ?? DefaultJsonOptions);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "InMemory store helpers are for development/testing. Use provided serializer or Redis for production AOT.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "InMemory store helpers are for development/testing. Use provided serializer or Redis for production AOT.")]
    public static T? Deserialize<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] T>(string data, IMessageSerializer? serializer = null)
    {
        if (serializer != null)
        {
            var bytes = Convert.FromBase64String(data);
            return serializer.Deserialize<T>(bytes);
        }
        return JsonSerializer.Deserialize<T>(data, DefaultJsonOptions);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed. Use MemoryPack serializer for AOT or provide JsonSerializerContext.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("JSON serialization may require runtime code generation. Use MemoryPack serializer for AOT or use source-generated JsonSerializerContext.")]
    public static T? DeserializeJson<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] T>(string data, JsonSerializerOptions? options = null)
        => JsonSerializer.Deserialize<T>(data, options ?? DefaultJsonOptions);

    public static bool TryDeserialize<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] T>(string data, out T? result, IMessageSerializer? serializer = null)
    {
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

    public static JsonSerializerOptions GetDefaultJsonOptions() => DefaultJsonOptions;
}
