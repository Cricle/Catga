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
    public abstract void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        T value,
        IBufferWriter<byte> bufferWriter);

    /// <summary>
    /// Deserialize from span (zero-copy)
    /// </summary>
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
    public virtual T Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(byte[] data)
        => Deserialize<T>(data.AsSpan());

    /// <summary>
    /// Serialize object to byte array (with runtime type)
    /// Must be implemented by concrete serializers without reflection.
    /// </summary>
    public abstract byte[] Serialize(object value, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type);

    /// <summary>
    /// Deserialize from byte array (with runtime type)
    /// </summary>
    public abstract object? Deserialize(byte[] data, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type);

    /// <summary>
    /// Deserialize from ReadOnlySpan (with runtime type, zero-copy)
    /// </summary>
    public abstract object? Deserialize(ReadOnlySpan<byte> data, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type);

    /// <summary>
    /// Serialize object to buffer writer (with runtime type, zero-allocation)
    /// </summary>
    public abstract void Serialize(object value, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type, IBufferWriter<byte> bufferWriter);
}

