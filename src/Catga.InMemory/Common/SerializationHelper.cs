using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Catga.Serialization;

namespace Catga.Common;

/// <summary>
/// Common serialization helper methods
/// Reduces code duplication across behaviors and stores
/// Provides consistent JSON options and zero-allocation serialization
/// </summary>
public static class SerializationHelper
{
    /// <summary>
    /// Default JSON serializer options (AOT-friendly)
    /// </summary>
    private static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>
    /// Serialize object using provided serializer or fallback to JSON
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [RequiresDynamicCode("Serialization may require runtime code generation")]
    [RequiresUnreferencedCode("Serialization may require types that cannot be statically analyzed")]
    public static string Serialize<T>(T obj, IMessageSerializer? serializer = null)
    {
        if (serializer != null)
        {
            var bytes = serializer.Serialize(obj);
            return Convert.ToBase64String(bytes);
        }

        // Fallback to JsonSerializer with default options
        return JsonSerializer.Serialize(obj, DefaultJsonOptions);
    }

    /// <summary>
    /// Serialize object with custom JSON options
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed")]
    public static string SerializeJson<T>(T obj, JsonSerializerOptions? options = null)
    {
        return JsonSerializer.Serialize(obj, options ?? DefaultJsonOptions);
    }

    /// <summary>
    /// Deserialize object using provided serializer or fallback to JSON
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [RequiresUnreferencedCode("Deserialization may require types that cannot be statically analyzed")]
    public static T? Deserialize<T>(string data, IMessageSerializer? serializer = null)
    {
        if (serializer != null)
        {
            var bytes = Convert.FromBase64String(data);
            return serializer.Deserialize<T>(bytes);
        }

        // Fallback to JsonSerializer with default options
        return JsonSerializer.Deserialize<T>(data, DefaultJsonOptions);
    }

    /// <summary>
    /// Deserialize object with custom JSON options
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [RequiresUnreferencedCode("JSON deserialization may require types that cannot be statically analyzed")]
    public static T? DeserializeJson<T>(string data, JsonSerializerOptions? options = null)
    {
        return JsonSerializer.Deserialize<T>(data, options ?? DefaultJsonOptions);
    }

    /// <summary>
    /// Try deserialize with exception handling
    /// </summary>
    public static bool TryDeserialize<T>(
        string data,
        out T? result,
        IMessageSerializer? serializer = null)
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

    /// <summary>
    /// Try deserialize JSON with exception handling
    /// </summary>
    public static bool TryDeserializeJson<T>(
        string data,
        out T? result,
        JsonSerializerOptions? options = null)
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

    /// <summary>
    /// Get default JSON serializer options (for custom usage)
    /// </summary>
    public static JsonSerializerOptions GetDefaultJsonOptions() => DefaultJsonOptions;
}
