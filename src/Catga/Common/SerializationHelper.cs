using System.Runtime.CompilerServices;
using System.Text.Json;
using Catga.Serialization;

namespace Catga.Common;

/// <summary>
/// Common serialization helper methods
/// Reduces code duplication across behaviors
/// </summary>
public static class SerializationHelper
{
    /// <summary>
    /// Serialize object using provided serializer or fallback to JSON
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Serialize<T>(T obj, IMessageSerializer? serializer = null)
    {
        if (serializer != null)
        {
            var bytes = serializer.Serialize(obj);
            return Convert.ToBase64String(bytes);
        }

        // Fallback to JsonSerializer
        return JsonSerializer.Serialize(obj);
    }

    /// <summary>
    /// Deserialize object using provided serializer or fallback to JSON
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? Deserialize<T>(string data, IMessageSerializer? serializer = null)
    {
        if (serializer != null)
        {
            var bytes = Convert.FromBase64String(data);
            return serializer.Deserialize<T>(bytes);
        }

        // Fallback to JsonSerializer
        return JsonSerializer.Deserialize<T>(data);
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
}
