using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Pooling;
using Catga.Serialization;
using MemoryPack;

namespace Catga.Serialization.MemoryPack;

/// <summary>
/// MemoryPack serializer with pooling support (high-performance binary, fully AOT-compatible)
/// </summary>
/// <remarks>
/// <para>
/// MemoryPack is the recommended serializer for Native AOT scenarios.
/// It uses source generators for zero-reflection serialization.
/// </para>
/// <para>
/// Memory Optimization:
/// - Inherits from MessageSerializerBase for zero-allocation serialization
/// - Uses MemoryPoolManager for pooled buffer management
/// - Native Span/Memory support for zero-copy operations
/// - No reflection, fully AOT-safe
/// - All pooling logic provided by base class (DRY principle)
/// </para>
/// <para>
/// Usage:
/// <code>
/// [MemoryPackable]
/// public partial class MyMessage
/// {
///     public string Name { get; set; }
/// }
///
/// services.AddCatga().UseMemoryPackSerializer();
/// </code>
/// </para>
/// </remarks>
public class MemoryPackMessageSerializer : MessageSerializerBase
{
    /// <summary>Create MemoryPack serializer with shared pool manager</summary>
    public MemoryPackMessageSerializer() 
        : this(null) { }

    /// <summary>Create MemoryPack serializer with custom pool manager</summary>
    public MemoryPackMessageSerializer(MemoryPoolManager? poolManager)
        : base(poolManager) { }

    public override string Name => "MemoryPack";

    #region Core Methods (Required by Base Class)

    /// <summary>
    /// Core serialization - serialize value to buffer writer
    /// </summary>
    public override void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        T value,
        IBufferWriter<byte> bufferWriter)
        => MemoryPackSerializer.Serialize(bufferWriter, value);

    /// <summary>
    /// Core deserialization - deserialize value from span
    /// </summary>
    public override T Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        ReadOnlySpan<byte> data)
        => MemoryPackSerializer.Deserialize<T>(data)!;

    /// <summary>
    /// Estimate serialized size for buffer allocation
    /// </summary>
    public override int GetSizeEstimate<T>(T value) => 128;

    #endregion

    #region Non-Generic Overloads (MemoryPack Support)

    /// <summary>
    /// Non-generic serialization (requires MemoryPackable attribute)
    /// </summary>
    public override byte[] Serialize(
        object? value,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        => MemoryPackSerializer.Serialize(type, value);

    /// <summary>
    /// Non-generic deserialization (requires MemoryPackable attribute)
    /// </summary>
    public override object? Deserialize(
        byte[] data,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        => MemoryPackSerializer.Deserialize(type, data);

    /// <summary>
    /// Non-generic serialization to buffer (requires MemoryPackable attribute)
    /// </summary>
    /// <remarks>
    /// Note: MemoryPack's non-generic API doesn't support direct IBufferWriter serialization.
    /// This implementation serializes to byte[] first, then writes to buffer.
    /// For best performance, use generic Serialize&lt;T&gt; methods.
    /// </remarks>
    public override void Serialize(
        object? value,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type,
        IBufferWriter<byte> bufferWriter)
    {
        // MemoryPack non-generic doesn't support IBufferWriter directly
        // Serialize to byte[] and write to buffer
        var bytes = MemoryPackSerializer.Serialize(type, value);
        bufferWriter.Write(bytes);
    }

    /// <summary>
    /// Non-generic deserialization from span (requires MemoryPackable attribute)
    /// </summary>
    /// <remarks>
    /// Note: MemoryPack's non-generic API requires byte[].
    /// This implementation converts span to array.
    /// For best performance, use generic Deserialize&lt;T&gt; methods.
    /// </remarks>
    public override object? Deserialize(
        ReadOnlySpan<byte> data,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        => MemoryPackSerializer.Deserialize(type, data.ToArray());

    /// <summary>
    /// Batch serialization - serialize multiple values with length prefixes
    /// </summary>
    /// <remarks>
    /// MemoryPack batch format:
    /// [Count: 4 bytes] [Item1Length: 4 bytes] [Item1Data] [Item2Length: 4 bytes] [Item2Data] ...
    /// </remarks>
    public override int SerializeBatch<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        IEnumerable<T> values,
        IBufferWriter<byte> bufferWriter)
    {
        int totalBytes = 0;
        Span<byte> lengthBuffer = stackalloc byte[4]; // Reuse buffer for all length writes

        // Write count prefix (4 bytes)
        var count = values is ICollection<T> collection ? collection.Count : values.Count();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, count);
        bufferWriter.Write(lengthBuffer);
        totalBytes += 4;

        // Serialize each item with length prefix
        foreach (var value in values)
        {
            using var itemWriter = PoolManager.RentBufferWriter();
            MemoryPackSerializer.Serialize(itemWriter, value);

            // Write item length (4 bytes)
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, itemWriter.WrittenCount);
            bufferWriter.Write(lengthBuffer);
            totalBytes += 4;

            // Write item data
            bufferWriter.Write(itemWriter.WrittenSpan);
            totalBytes += itemWriter.WrittenCount;
        }

        return totalBytes;
    }

    #endregion
}
