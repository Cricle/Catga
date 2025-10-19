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
/// - Zero-allocation serialization with pooled buffers
/// - Native Span/Memory support
/// - No reflection, fully AOT-safe
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
public class MemoryPackMessageSerializer : IPooledMessageSerializer
{
    private readonly MemoryPoolManager _poolManager;

    /// <summary>Create MemoryPack serializer with shared pool manager</summary>
    public MemoryPackMessageSerializer() : this(MemoryPoolManager.Shared) { }

    /// <summary>Create MemoryPack serializer with custom pool manager</summary>
    public MemoryPackMessageSerializer(MemoryPoolManager poolManager)
    {
        _poolManager = poolManager ?? throw new ArgumentNullException(nameof(poolManager));
    }

    public string Name => "MemoryPack";

    public byte[] Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value)
        => MemoryPackSerializer.Serialize(value);

    public byte[] Serialize(object? value, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        => MemoryPackSerializer.Serialize(type, value);

    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(byte[] data)
        => Deserialize<T>(data.AsSpan());

    public object? Deserialize(byte[] data, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        => MemoryPackSerializer.Deserialize(type, data);

    public void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value, IBufferWriter<byte> bufferWriter)
        => MemoryPackSerializer.Serialize(bufferWriter, value);

    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(ReadOnlySpan<byte> data)
        => MemoryPackSerializer.Deserialize<T>(data);

    public int GetSizeEstimate<T>(T value) => 128;

    // === IMessageSerializer 新方法 (全部 AOT-safe) ===

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IMemoryOwner<byte> SerializeToMemory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value)
    {
        // MemoryPack can serialize directly to pooled memory
        using var writer = _poolManager.RentBufferWriter();
        MemoryPackSerializer.Serialize(writer, value);
        
        var owner = _poolManager.RentMemory(writer.WrittenCount);
        writer.WrittenSpan.CopyTo(owner.Memory.Span);
        return owner;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(ReadOnlyMemory<byte> data)
        => MemoryPackSerializer.Deserialize<T>(data.Span);

    /// <inheritdoc />
    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(ReadOnlySequence<byte> data)
    {
        if (data.IsSingleSegment)
            return MemoryPackSerializer.Deserialize<T>(data.FirstSpan);

        // Multi-segment: rent buffer and copy
        using var owner = _poolManager.RentMemory((int)data.Length);
        data.CopyTo(owner.Memory.Span);
        return MemoryPackSerializer.Deserialize<T>(owner.Memory.Span);
    }

    // === IBufferedMessageSerializer 新方法 (全部 AOT-safe) ===

    /// <inheritdoc />
    public void Serialize(object? value, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type, IBufferWriter<byte> bufferWriter)
    {
        // MemoryPack's non-generic version already uses IBufferWriter
        var bytes = MemoryPackSerializer.Serialize(type, value);
        bufferWriter.Write(bytes);
    }

    /// <inheritdoc />
    public object? Deserialize(ReadOnlySpan<byte> data, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        => MemoryPackSerializer.Deserialize(type, data.ToArray());

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySerialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value, Span<byte> destination, out int bytesWritten)
    {
        try
        {
            // Use pooled writer
            using var pooledWriter = _poolManager.RentBufferWriter(destination.Length);
            MemoryPackSerializer.Serialize(pooledWriter, value);
            
            if (pooledWriter.WrittenCount > destination.Length)
            {
                bytesWritten = 0;
                return false;
            }
            
            pooledWriter.WrittenSpan.CopyTo(destination);
            bytesWritten = pooledWriter.WrittenCount;
            return true;
        }
        catch
        {
            bytesWritten = 0;
            return false;
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value, Memory<byte> destination, out int bytesWritten)
    {
        using var pooledWriter = _poolManager.RentBufferWriter(destination.Length);
        MemoryPackSerializer.Serialize(pooledWriter, value);
        
        if (pooledWriter.WrittenCount > destination.Length)
            throw new InvalidOperationException($"Destination buffer too small. Required: {pooledWriter.WrittenCount}, Available: {destination.Length}");
        
        pooledWriter.WrittenSpan.CopyTo(destination.Span);
        bytesWritten = pooledWriter.WrittenCount;
    }

    /// <inheritdoc />
    public int SerializeBatch<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(IEnumerable<T> values, IBufferWriter<byte> bufferWriter)
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
            using var itemWriter = _poolManager.RentBufferWriter();
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

    // === IPooledMessageSerializer 方法 (全部 AOT-safe) ===

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PooledBuffer SerializePooled<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value)
    {
        var owner = SerializeToMemory(value);
        return new PooledBuffer(owner, (int)owner.Memory.Length);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? DeserializePooled<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(ReadOnlySequence<byte> data)
        => Deserialize<T>(data);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IPooledBufferWriter<byte> GetPooledWriter(int initialCapacity = 256)
        => _poolManager.RentBufferWriter(initialCapacity);
}

