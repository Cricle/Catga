using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Catga;
using MemoryPack;

namespace Catga.Serialization.MemoryPack;

/// <summary>
/// MemoryPack serializer (high-performance binary, fully AOT-compatible)
/// </summary>
/// <remarks>
/// MemoryPack is the recommended serializer for Native AOT scenarios.
/// Usage:
/// <code>
/// [MemoryPackable]
/// public partial class MyMessage
/// {
///     public string Name { get; set; }
/// }
/// </code>
/// </remarks>
public class MemoryPackMessageSerializer : MessageSerializerBase
{
    public override string Name => "MemoryPack";

    /// <summary>
    /// Serialize to buffer writer
    /// </summary>
    public override void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        T value,
        IBufferWriter<byte> bufferWriter)
        => MemoryPackSerializer.Serialize(bufferWriter, value);

    /// <summary>
    /// Deserialize from span
    /// </summary>
    public override T Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        ReadOnlySpan<byte> data)
        => MemoryPackSerializer.Deserialize<T>(data)!;

    /// <summary>
    /// Estimate serialized size for buffer allocation
    /// </summary>
    protected override int GetSizeEstimate<T>(T value) => 128;
}
