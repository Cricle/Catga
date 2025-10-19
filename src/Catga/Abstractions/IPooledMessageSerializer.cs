using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Catga.Serialization;

/// <summary>
/// Pooled message serializer with recyclable buffers and automatic disposal
/// </summary>
/// <remarks>
/// <para>
/// Extends IBufferedMessageSerializer with pooled memory management.
/// All buffers are rented from shared pools and automatically returned on disposal.
/// </para>
/// <para>
/// AOT Compatibility:
/// - All generic methods are AOT-friendly
/// - Uses MemoryPool and ArrayPool (AOT-safe)
/// - No reflection in pooling infrastructure
/// </para>
/// <para>
/// Usage Pattern:
/// <code>
/// using var pooled = serializer.SerializePooled(message);
/// await SendAsync(pooled.Memory);  // pooled.Memory is valid
/// // Automatically returned to pool on dispose
/// </code>
/// </para>
/// </remarks>
public interface IPooledMessageSerializer : IBufferedMessageSerializer
{
    /// <summary>
    /// Serialize using pooled buffer (caller must dispose)
    /// </summary>
    /// <remarks>
    /// AOT-friendly. Returns PooledBuffer that must be disposed to return memory to pool.
    /// Zero allocation except for the PooledBuffer struct itself.
    /// </remarks>
    PooledBuffer SerializePooled<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value);

    /// <summary>
    /// Deserialize using pooled buffer reader (for large data)
    /// </summary>
    /// <remarks>
    /// AOT-friendly. Optimized for multi-segment data in pipelines.
    /// Automatically handles buffer copying if needed.
    /// </remarks>
    T? DeserializePooled<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        ReadOnlySequence<byte> data);

    /// <summary>
    /// Get pooled buffer writer (caller must dispose)
    /// </summary>
    /// <remarks>
    /// AOT-friendly. Returns IPooledBufferWriter that must be disposed.
    /// Use this for advanced scenarios where you need direct buffer access.
    /// </remarks>
    IPooledBufferWriter<byte> GetPooledWriter(int initialCapacity = 256);
}

/// <summary>
/// Pooled buffer with automatic disposal and memory return
/// </summary>
/// <remarks>
/// Lightweight struct that wraps pooled memory.
/// Must be disposed to return memory to pool.
/// AOT-safe, no reflection.
/// </remarks>
public readonly struct PooledBuffer : IDisposable
{
    private readonly IMemoryOwner<byte>? _owner;
    private readonly int _length;

    internal PooledBuffer(IMemoryOwner<byte> owner, int length)
    {
        _owner = owner;
        _length = length;
    }

    /// <summary>
    /// Get the pooled memory (valid until Dispose)
    /// </summary>
    public ReadOnlyMemory<byte> Memory => _owner?.Memory.Slice(0, _length) ?? ReadOnlyMemory<byte>.Empty;

    /// <summary>
    /// Get the length of valid data
    /// </summary>
    public int Length => _length;

    /// <summary>
    /// Return memory to pool
    /// </summary>
    public void Dispose()
    {
        _owner?.Dispose();
    }
}

/// <summary>
/// Pooled buffer writer interface with automatic disposal
/// </summary>
/// <remarks>
/// Extends IBufferWriter with pooling and disposal.
/// Must be disposed to return buffer to pool.
/// AOT-safe.
/// </remarks>
public interface IPooledBufferWriter<T> : IBufferWriter<T>, IDisposable
{
    /// <summary>
    /// Get the written memory (valid until Dispose or Clear)
    /// </summary>
    ReadOnlyMemory<T> WrittenMemory { get; }

    /// <summary>
    /// Get the written span (valid until Dispose or Clear)
    /// </summary>
    ReadOnlySpan<T> WrittenSpan { get; }

    /// <summary>
    /// Get the count of written items
    /// </summary>
    int WrittenCount { get; }

    /// <summary>
    /// Clear the buffer for reuse (does not return to pool)
    /// </summary>
    void Clear();
}

