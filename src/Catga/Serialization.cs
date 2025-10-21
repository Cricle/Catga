using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Abstractions;
using Catga.Core;

namespace Catga;

/// <summary>
/// Base class for message serializers (AOT-safe, minimal API)
/// </summary>
/// <remarks>
/// Derived classes only need to implement 3 core methods:
/// - Serialize to IBufferWriter
/// - Deserialize from ReadOnlySpan
/// - GetSizeEstimate for buffer allocation
/// </remarks>
public abstract class MessageSerializerBase : IMessageSerializer
{
    /// <summary>
    /// Serializer name (e.g., "JSON", "MemoryPack")
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Serialize to buffer writer (zero-allocation)
    /// </summary>
    [RequiresDynamicCode("Serialization may use reflection for certain types")]
    [RequiresUnreferencedCode("Serialization may require unreferenced code for certain types")]
    public abstract void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        T value,
        IBufferWriter<byte> bufferWriter);

    /// <summary>
    /// Deserialize from span (zero-copy)
    /// </summary>
    [RequiresDynamicCode("Deserialization may use reflection for certain types")]
    [RequiresUnreferencedCode("Deserialization may require unreferenced code for certain types")]
    public abstract T Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        ReadOnlySpan<byte> data);

    /// <summary>
    /// Estimate serialized size for buffer allocation
    /// </summary>
    protected abstract int GetSizeEstimate<T>(T value);

    /// <summary>
    /// Serialize to byte[] using pooled buffer
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [RequiresDynamicCode("Serialization may use reflection for certain types")]
    [RequiresUnreferencedCode("Serialization may require unreferenced code for certain types")]
    public virtual byte[] Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value)
    {
        using var writer = MemoryPoolManager.RentBufferWriter<byte>(GetSizeEstimate(value));
        Serialize(value, writer);
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Deserialize from byte[]
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [RequiresDynamicCode("Deserialization may use reflection for certain types")]
    [RequiresUnreferencedCode("Deserialization may require unreferenced code for certain types")]
    public virtual T Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(byte[] data)
        => Deserialize<T>(data.AsSpan());

    /// <summary>
    /// Serialize object to byte array (with runtime type)
    /// </summary>
    public virtual byte[] Serialize(object value, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(type);

        // Use reflection to call generic Serialize<T>
        var method = typeof(MessageSerializerBase).GetMethod(nameof(Serialize), 1, new[] { type })!;
        var genericMethod = method.MakeGenericMethod(type);
        return (byte[])genericMethod.Invoke(this, new[] { value })!;
    }

    /// <summary>
    /// Deserialize from byte array (with runtime type)
    /// </summary>
    public virtual object? Deserialize(byte[] data, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(type);

        // Use reflection to call generic Deserialize<T>
        var method = typeof(MessageSerializerBase).GetMethod(nameof(Deserialize), new[] { typeof(byte[]) })!;
        var genericMethod = method.MakeGenericMethod(type);
        return genericMethod.Invoke(this, new object[] { data });
    }

    /// <summary>
    /// Deserialize from ReadOnlySpan (with runtime type, zero-copy)
    /// </summary>
    public virtual object? Deserialize(ReadOnlySpan<byte> data, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        // Convert ReadOnlySpan to byte[] since we can't box spans
        var dataArray = data.ToArray();
        
        // Use reflection to call generic Deserialize<T>
        var method = typeof(MessageSerializerBase).GetMethod(nameof(Deserialize), new[] { typeof(byte[]) })!;
        var genericMethod = method.MakeGenericMethod(type);
        return genericMethod.Invoke(this, new object[] { dataArray });
    }

    /// <summary>
    /// Serialize object to buffer writer (with runtime type, zero-allocation)
    /// </summary>
    public virtual void Serialize(object value, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type, IBufferWriter<byte> bufferWriter)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(bufferWriter);

        // Use reflection to call generic Serialize<T>
        var method = typeof(MessageSerializerBase).GetMethod(nameof(Serialize), new[] { type, typeof(IBufferWriter<byte>) })!;
        var genericMethod = method.MakeGenericMethod(type);
        genericMethod.Invoke(this, new[] { value, bufferWriter });
    }
}

/// <summary>
/// Serialization helper with Base64 encoding (AOT-safe)
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
        using var pooled = MemoryPoolManager.RentArray<byte>(base64Length);
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

