using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Catga.Serialization;
using MemoryPack;

namespace Catga.Serialization.MemoryPack;

/// <summary>
/// MemoryPack message serializer - High-performance binary serialization (AOT friendly)
/// Optimized with buffer pooling for reduced allocations
/// </summary>
public class MemoryPackMessageSerializer : IBufferedMessageSerializer
{
    public string Name => "MemoryPack";

    #region IMessageSerializer (legacy, allocating)

    [RequiresUnreferencedCode("Serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Serialization may require runtime code generation")]
    public byte[] Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] T>(T value)
    {
        // MemoryPack already uses ArrayPool internally
        return MemoryPackSerializer.Serialize(value);
    }

    [RequiresUnreferencedCode("Deserialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Deserialization may require runtime code generation")]
    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicConstructors)] T>(byte[] data)
    {
        // Optimized: Deserialize from ReadOnlySpan (zero-copy)
        return Deserialize<T>(data.AsSpan());
    }

    #endregion

    #region IBufferedMessageSerializer (optimized, pooled)

    [RequiresUnreferencedCode("Serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Serialization may require runtime code generation")]
    public void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] T>(
        T value,
        IBufferWriter<byte> bufferWriter)
    {
        // MemoryPack supports IBufferWriter directly (zero-copy)
        MemoryPackSerializer.Serialize(bufferWriter, value);
    }

    [RequiresUnreferencedCode("Deserialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Deserialization may require runtime code generation")]
    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        ReadOnlySpan<byte> data)
    {
        // MemoryPack supports ReadOnlySpan directly (zero-copy)
        return MemoryPackSerializer.Deserialize<T>(data);
    }

    public int GetSizeEstimate<T>(T value)
    {
        // MemoryPack is compact binary format
        // Conservative estimate for small objects
        return 128;
    }

    #endregion
}

