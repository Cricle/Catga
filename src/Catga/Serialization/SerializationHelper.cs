using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Abstractions;
using Catga.Pooling;

namespace Catga.Serialization;

/// <summary>
/// Serialization helper with memory pooling (AOT-safe)
/// </summary>
public static class SerializationHelper
{
    private const int StackAllocThreshold = 256;

    /// <summary>
    /// Serialize to Base64 string
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        T obj,
        IMessageSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        var bytes = serializer.Serialize(obj);
        return EncodeBase64(bytes);
    }

    /// <summary>
    /// Deserialize from Base64 string
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        string data,
        IMessageSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        if (string.IsNullOrEmpty(data))
            return default;

        var bytes = DecodeBase64(data);
        return serializer.Deserialize<T>(bytes);
    }

    /// <summary>
    /// Try deserialize
    /// </summary>
    public static bool TryDeserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        string data,
        out T? result,
        IMessageSerializer serializer)
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

    /// <summary>
    /// Encode bytes to Base64 string using pooled buffer
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string EncodeBase64(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            return string.Empty;

        int base64Length = Base64.GetMaxEncodedToUtf8Length(data.Length);

        // Small data: use stackalloc
        if (base64Length <= StackAllocThreshold)
        {
            Span<byte> buffer = stackalloc byte[base64Length];
            if (Base64.EncodeToUtf8(data, buffer, out _, out int written) == OperationStatus.Done)
                return System.Text.Encoding.UTF8.GetString(buffer[..written]);
        }

        // Large data: use ArrayPool
        using var pooled = MemoryPoolManager.RentArray(base64Length);
        if (Base64.EncodeToUtf8(data, pooled.Span, out _, out int pooledWritten) == OperationStatus.Done)
            return System.Text.Encoding.UTF8.GetString(pooled.Span[..pooledWritten]);

        // Fallback
        return Convert.ToBase64String(data);
    }

    /// <summary>
    /// Decode Base64 string to bytes
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte[] DecodeBase64(string base64)
    {
        if (string.IsNullOrEmpty(base64))
            return Array.Empty<byte>();

        // Estimate decoded size
        int maxLength = (base64.Length * 3) / 4;

        // Small data: use stackalloc
        if (maxLength <= StackAllocThreshold)
        {
            Span<byte> utf8 = stackalloc byte[base64.Length];
            int utf8Count = System.Text.Encoding.UTF8.GetBytes(base64, utf8);

            Span<byte> buffer = stackalloc byte[maxLength];
            if (Base64.DecodeFromUtf8(utf8[..utf8Count], buffer, out _, out int written) == OperationStatus.Done)
                return buffer[..written].ToArray();
        }

        // Fallback
        return Convert.FromBase64String(base64);
    }
}
