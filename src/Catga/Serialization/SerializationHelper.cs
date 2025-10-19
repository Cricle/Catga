using Catga.Abstractions;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Catga.Serialization;

/// <summary>Serialization helper - requires explicit serializer for type safety and AOT compatibility</summary>
public static class SerializationHelper
{
    /// <summary>Serialize object using provided serializer (required)</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]T>(T obj, IMessageSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        var bytes = serializer.Serialize(obj);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>Deserialize object using provided serializer (required)</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string data, IMessageSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        var bytes = Convert.FromBase64String(data);
        return serializer.Deserialize<T>(bytes);
    }

    /// <summary>Try deserialize using provided serializer (required)</summary>
    public static bool TryDeserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string data, out T? result, IMessageSerializer serializer)
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
}
