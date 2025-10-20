using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Abstractions;
using Catga.Pooling;

namespace Catga.Serialization;

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
    #region Abstract Methods (Must Implement)

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

    #endregion

    #region Common Implementations

    /// <summary>
    /// Serialize to byte[] using pooled buffer
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual byte[] Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value)
    {
        using var writer = MemoryPoolManager.RentBufferWriter(GetSizeEstimate(value));
        Serialize(value, writer);
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Deserialize from byte[]
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual T Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(byte[] data)
        => Deserialize<T>(data.AsSpan());

    #endregion
}
