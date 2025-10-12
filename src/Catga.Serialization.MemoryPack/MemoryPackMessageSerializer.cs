using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Catga.Serialization;
using MemoryPack;

namespace Catga.Serialization.MemoryPack;

/// <summary>MemoryPack serializer (high-performance binary, AOT-friendly, zero-copy)</summary>
public class MemoryPackMessageSerializer : IBufferedMessageSerializer
{
    public string Name => "MemoryPack";

    public byte[] Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value)
        => MemoryPackSerializer.Serialize(value);

    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(byte[] data)
        => Deserialize<T>(data.AsSpan());

    public void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value, IBufferWriter<byte> bufferWriter)
        => MemoryPackSerializer.Serialize(bufferWriter, value);

    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(ReadOnlySpan<byte> data)
        => MemoryPackSerializer.Deserialize<T>(data);

    public int GetSizeEstimate<T>(T value) => 128;
}

