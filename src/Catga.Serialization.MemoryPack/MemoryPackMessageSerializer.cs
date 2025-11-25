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

    // --- Runtime-type overloads (no reflection) ---

    public override byte[] Serialize(object value, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(type);
        return MemoryPackSerializer.Serialize(type, value)!;
    }

    public override object? Deserialize(byte[] data, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(type);
        return MemoryPackSerializer.Deserialize(type, data);
    }

    public override object? Deserialize(ReadOnlySpan<byte> data, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return MemoryPackSerializer.Deserialize(type, data);
    }

    public override void Serialize(object value, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type, IBufferWriter<byte> bufferWriter)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(bufferWriter);

        // MemoryPack doesn't expose a non-generic writer overload with Type; write the produced bytes
        var bytes = MemoryPackSerializer.Serialize(type, value)!;
        var span = bufferWriter.GetSpan(bytes.Length);
        bytes.CopyTo(span);
        bufferWriter.Advance(bytes.Length);
    }
}
