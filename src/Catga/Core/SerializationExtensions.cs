using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using Catga.Abstractions;

namespace Catga.Core;

/// <summary>
/// Serialization extension methods for common patterns (DRY principle)
/// </summary>
/// <remarks>
/// Provides helper methods to reduce boilerplate code for:
/// - Serialization to UTF-8 JSON string
/// - Deserialization from UTF-8 JSON string
/// - Safe deserialization with exception handling
///
/// All methods are AOT-compatible and use stack allocation where possible.
/// </remarks>
public static class SerializationExtensions
{
    /// <summary>
    /// Serialize object to UTF-8 JSON string (optimized with ArrayPool)
    /// </summary>
    /// <typeparam name="T">Type to serialize</typeparam>
    /// <param name="serializer">Message serializer</param>
    /// <param name="value">Value to serialize</param>
    /// <returns>UTF-8 JSON string</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string SerializeToJson<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(this IMessageSerializer serializer, T value)
    {
        if (value == null) return string.Empty;
        var bytes = serializer.Serialize(value);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Deserialize from UTF-8 JSON string (optimized with ArrayPool)
    /// </summary>
    /// <typeparam name="T">Type to deserialize</typeparam>
    /// <param name="serializer">Message serializer</param>
    /// <param name="json">UTF-8 JSON string</param>
    /// <returns>Deserialized object, or default if null/empty</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? DeserializeFromJson<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(this IMessageSerializer serializer, string json)
    {
        if (string.IsNullOrEmpty(json)) return default;

        var bytes = Encoding.UTF8.GetBytes(json);
        return serializer.Deserialize<T>(bytes);
    }

    /// <summary>
    /// Try deserialize with exception handling (safe)
    /// </summary>
    /// <typeparam name="T">Type to deserialize</typeparam>
    /// <param name="serializer">Message serializer</param>
    /// <param name="data">Byte array to deserialize</param>
    /// <param name="result">Deserialized result (output)</param>
    /// <returns>True if successful, false otherwise</returns>
    public static bool TryDeserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        this IMessageSerializer serializer,
        byte[] data,
        out T? result)
    {
        if (data == null || data.Length == 0)
        {
            result = default;
            return false;
        }

        try
        {
            result = serializer.Deserialize<T>(data);
            return result != null;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    /// <summary>
    /// Try deserialize from JSON string with exception handling (safe)
    /// </summary>
    /// <typeparam name="T">Type to deserialize</typeparam>
    /// <param name="serializer">Message serializer</param>
    /// <param name="json">JSON string</param>
    /// <param name="result">Deserialized result (output)</param>
    /// <returns>True if successful, false otherwise</returns>
    public static bool TryDeserializeFromJson<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        this IMessageSerializer serializer,
        string json,
        out T? result)
    {
        if (string.IsNullOrEmpty(json))
        {
            result = default;
            return false;
        }

        try
        {
            result = DeserializeFromJson<T>(serializer, json);
            return result != null;
        }
        catch
        {
            result = default;
            return false;
        }
    }
}

