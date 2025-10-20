using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Abstractions;
using Catga.Pooling;

namespace Catga.Serialization;

/// <summary>
/// Base class for message serializers with common pooling logic (AOT-safe, zero duplication)
/// </summary>
/// <remarks>
/// <para>
/// Provides common implementations for pooled serialization patterns, eliminating ~200 lines
/// of duplicate code per serializer implementation.
/// </para>
/// <para>
/// Derived classes only need to implement 3 core methods:
/// - <see cref="Serialize{T}(T, IBufferWriter{byte})"/> - Core serialization to buffer
/// - <see cref="Deserialize{T}(ReadOnlySpan{byte})"/> - Core deserialization from span
/// - <see cref="GetSizeEstimate{T}"/> - Estimate serialized size for buffer allocation
/// </para>
/// <para>
/// All pooling, buffer management, and multi-segment handling is provided by the base class.
/// This ensures consistent behavior across all serializers and reduces maintenance burden.
/// </para>
/// <para>
/// AOT Compatibility: Fully compatible with Native AOT. No reflection, no dynamic code generation.
/// All generic methods use DynamicallyAccessedMembersAttribute for trim safety.
/// </para>
/// <para>
/// Thread Safety: Instances are thread-safe as long as the underlying serializer is thread-safe.
/// </para>
/// </remarks>
public abstract class MessageSerializerBase : IPooledMessageSerializer
{
    /// <summary>
    /// Memory pool manager for buffer allocation and reuse
    /// </summary>
    protected readonly MemoryPoolManager PoolManager;

    /// <summary>
    /// Initialize base serializer with optional pool manager
    /// </summary>
    /// <param name="poolManager">Pool manager for buffer allocation (null = use shared instance)</param>
    protected MessageSerializerBase(MemoryPoolManager? poolManager = null)
    {
        PoolManager = poolManager ?? MemoryPoolManager.Shared;
    }

    #region Abstract Methods (Must Implement)

    /// <summary>
    /// Serializer name (e.g., "JSON", "MemoryPack")
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Core serialization method - serialize value to buffer writer
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="value">Value to serialize</param>
    /// <param name="bufferWriter">Buffer writer to write serialized data</param>
    /// <remarks>
    /// This is the only serialization method that derived classes must implement.
    /// All other serialization methods delegate to this core method.
    /// </remarks>
    public abstract void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        T value,
        IBufferWriter<byte> bufferWriter);

    /// <summary>
    /// Core deserialization method - deserialize value from span
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="data">Serialized data</param>
    /// <returns>Deserialized value</returns>
    /// <remarks>
    /// This is the only deserialization method that derived classes must implement.
    /// All other deserialization methods delegate to this core method.
    /// </remarks>
    public abstract T Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        ReadOnlySpan<byte> data);

    /// <summary>
    /// Estimate serialized size for buffer allocation optimization
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="value">Value to estimate</param>
    /// <returns>Estimated size in bytes</returns>
    /// <remarks>
    /// Used to optimize initial buffer allocation. Doesn't need to be exact.
    /// Common values: 128 (MemoryPack), 256 (JSON).
    /// </remarks>
    public abstract int GetSizeEstimate<T>(T value);

    #endregion

    #region Common Implementations (DRY - No Duplication)

    /// <summary>
    /// Serialize to byte[] using pooled buffer writer
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="value">Value to serialize</param>
    /// <returns>Serialized byte array</returns>
    /// <remarks>
    /// AOT-safe. Uses pooled buffer writer to reduce GC pressure during serialization.
    /// The final array allocation is unavoidable for the return value.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual byte[] Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value)
    {
        using var writer = PoolManager.RentBufferWriter(GetSizeEstimate(value));
        Serialize(value, writer);
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Serialize to IMemoryOwner (zero-allocation path, caller must dispose)
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="value">Value to serialize</param>
    /// <returns>Memory owner containing serialized data (must dispose)</returns>
    /// <remarks>
    /// AOT-safe. Zero-allocation serialization path. Caller is responsible for disposing
    /// the returned IMemoryOwner to return memory to the pool.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual IMemoryOwner<byte> SerializeToMemory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value)
    {
        using var writer = PoolManager.RentBufferWriter(GetSizeEstimate(value));
        Serialize(value, writer);

        var owner = PoolManager.RentMemory(writer.WrittenCount);
        writer.WrittenSpan.CopyTo(owner.Memory.Span);
        return owner;
    }

    /// <summary>
    /// Deserialize from byte[]
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="data">Serialized data</param>
    /// <returns>Deserialized value</returns>
    /// <remarks>AOT-safe. Delegates to span-based deserialization.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual T Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(byte[] data)
        => Deserialize<T>(data.AsSpan());

    /// <summary>
    /// Deserialize from ReadOnlyMemory
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="data">Serialized data</param>
    /// <returns>Deserialized value</returns>
    /// <remarks>AOT-safe. Delegates to span-based deserialization.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual T Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(ReadOnlyMemory<byte> data)
        => Deserialize<T>(data.Span);

    /// <summary>
    /// Deserialize from ReadOnlySequence (handles multi-segment buffers with pooling)
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="data">Serialized data (potentially multi-segment)</param>
    /// <returns>Deserialized value</returns>
    /// <remarks>
    /// AOT-safe. For single-segment sequences, deserializes directly from the span.
    /// For multi-segment sequences, uses pooled memory to copy and deserialize.
    /// </remarks>
    public virtual T Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(ReadOnlySequence<byte> data)
    {
        if (data.IsSingleSegment)
            return Deserialize<T>(data.FirstSpan);

        // Multi-segment: rent pooled buffer and copy
        using var owner = PoolManager.RentMemory((int)data.Length);
        data.CopyTo(owner.Memory.Span);
        return Deserialize<T>(owner.Memory.Span);
    }

    /// <summary>
    /// Try to serialize to destination span (zero-allocation if destination is sufficient)
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="value">Value to serialize</param>
    /// <param name="destination">Destination span</param>
    /// <param name="bytesWritten">Number of bytes written</param>
    /// <returns>True if serialization succeeded, false if destination was too small</returns>
    /// <remarks>
    /// AOT-safe. Zero-allocation if destination span is large enough.
    /// Uses pooled buffer writer internally for serialization.
    /// </remarks>
    public virtual bool TrySerialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        T value,
        Span<byte> destination,
        out int bytesWritten)
    {
        try
        {
            using var pooledWriter = PoolManager.RentBufferWriter(destination.Length);
            Serialize(value, pooledWriter);

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

    /// <summary>
    /// Serialize to Memory destination (throws if insufficient space)
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="value">Value to serialize</param>
    /// <param name="destination">Destination memory</param>
    /// <param name="bytesWritten">Number of bytes written</param>
    /// <exception cref="InvalidOperationException">Thrown if destination is too small</exception>
    /// <remarks>AOT-safe. Uses pooled buffer writer internally.</remarks>
    public virtual void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        T value,
        Memory<byte> destination,
        out int bytesWritten)
    {
        using var pooledWriter = PoolManager.RentBufferWriter(destination.Length);
        Serialize(value, pooledWriter);

        if (pooledWriter.WrittenCount > destination.Length)
            throw new InvalidOperationException(
                $"Destination buffer too small. Required: {pooledWriter.WrittenCount}, Available: {destination.Length}");

        pooledWriter.WrittenSpan.CopyTo(destination.Span);
        bytesWritten = pooledWriter.WrittenCount;
    }

    /// <summary>
    /// Serialize to PooledBuffer (IPooledMessageSerializer interface)
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="value">Value to serialize</param>
    /// <returns>Pooled buffer containing serialized data (must dispose)</returns>
    /// <remarks>AOT-safe. Delegates to SerializeToMemory.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual PooledBuffer SerializePooled<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value)
    {
        var owner = SerializeToMemory(value);
        return new PooledBuffer(owner, owner.Memory.Length);
    }

    /// <summary>
    /// Deserialize from ReadOnlySequence (IPooledMessageSerializer interface)
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="data">Serialized data</param>
    /// <returns>Deserialized value</returns>
    /// <remarks>AOT-safe. Delegates to Deserialize(ReadOnlySequence).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual T DeserializePooled<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(ReadOnlySequence<byte> data)
        => Deserialize<T>(data);

    /// <summary>
    /// Get pooled buffer writer (IPooledMessageSerializer interface)
    /// </summary>
    /// <param name="initialCapacity">Initial capacity</param>
    /// <returns>Pooled buffer writer (must dispose)</returns>
    /// <remarks>AOT-safe.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual IPooledBufferWriter<byte> GetPooledWriter(int initialCapacity = 256)
        => PoolManager.RentBufferWriter(initialCapacity);

    #endregion

    #region Non-Generic Overloads (Optional - Override if needed)

    /// <summary>
    /// Non-generic serialization (optional override)
    /// </summary>
    /// <param name="value">Value to serialize</param>
    /// <param name="type">Value type</param>
    /// <returns>Serialized byte array</returns>
    /// <exception cref="NotSupportedException">Thrown if not overridden by derived class</exception>
    /// <remarks>
    /// Default implementation throws NotSupportedException. Override if your serializer
    /// supports non-generic serialization (e.g., System.Text.Json, MemoryPack).
    /// </remarks>
    public virtual byte[] Serialize(
        object? value,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        throw new NotSupportedException(
            $"{GetType().Name} does not support non-generic serialization. Use generic Serialize<T> instead, or override this method.");
    }

    /// <summary>
    /// Non-generic deserialization (optional override)
    /// </summary>
    /// <param name="data">Serialized data</param>
    /// <param name="type">Target type</param>
    /// <returns>Deserialized value</returns>
    /// <exception cref="NotSupportedException">Thrown if not overridden by derived class</exception>
    public virtual object? Deserialize(
        byte[] data,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        throw new NotSupportedException(
            $"{GetType().Name} does not support non-generic deserialization. Use generic Deserialize<T> instead, or override this method.");
    }

    /// <summary>
    /// Non-generic serialization to buffer (optional override)
    /// </summary>
    /// <param name="value">Value to serialize</param>
    /// <param name="type">Value type</param>
    /// <param name="bufferWriter">Buffer writer</param>
    /// <exception cref="NotSupportedException">Thrown if not overridden by derived class</exception>
    public virtual void Serialize(
        object? value,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type,
        IBufferWriter<byte> bufferWriter)
    {
        throw new NotSupportedException(
            $"{GetType().Name} does not support non-generic serialization. Use generic Serialize<T> instead, or override this method.");
    }

    /// <summary>
    /// Non-generic deserialization from span (optional override)
    /// </summary>
    /// <param name="data">Serialized data</param>
    /// <param name="type">Target type</param>
    /// <returns>Deserialized value</returns>
    /// <exception cref="NotSupportedException">Thrown if not overridden by derived class</exception>
    public virtual object? Deserialize(
        ReadOnlySpan<byte> data,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        throw new NotSupportedException(
            $"{GetType().Name} does not support non-generic deserialization. Use generic Deserialize<T> instead, or override this method.");
    }

    /// <summary>
    /// Batch serialization (optional override)
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="values">Values to serialize</param>
    /// <param name="bufferWriter">Buffer writer</param>
    /// <returns>Total bytes written</returns>
    /// <exception cref="NotSupportedException">Thrown if not overridden by derived class</exception>
    /// <remarks>
    /// Override if your serializer has special batch serialization support.
    /// </remarks>
    public virtual int SerializeBatch<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        IEnumerable<T> values,
        IBufferWriter<byte> bufferWriter)
    {
        throw new NotSupportedException(
            $"{GetType().Name} does not support batch serialization. Override this method to implement batch support.");
    }

    #endregion
}

