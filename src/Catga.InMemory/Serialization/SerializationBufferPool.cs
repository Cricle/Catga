using System.Buffers;
using System.Runtime.CompilerServices;

namespace Catga.Serialization;

/// <summary>
/// Serialization buffer pool to reduce allocations
/// Thread-safe, lock-free pooling using ArrayPool
/// </summary>
public static class SerializationBufferPool
{
    // Use shared pool for better memory efficiency
    private static readonly ArrayPool<byte> _pool = ArrayPool<byte>.Shared;

    /// <summary>
    /// Rent buffer from pool (minimum size)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] Rent(int minimumSize)
    {
        return _pool.Rent(minimumSize);
    }

    /// <summary>
    /// Return buffer to pool
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(byte[] buffer, bool clearArray = false)
    {
        _pool.Return(buffer, clearArray);
    }

    /// <summary>
    /// Pooled buffer scope (auto-return on dispose)
    /// Usage: using var buffer = SerializationBufferPool.RentScoped(1024);
    /// </summary>
    public static PooledBuffer RentScoped(int minimumSize)
    {
        return new PooledBuffer(minimumSize);
    }
}

/// <summary>
/// Pooled buffer with automatic return (IDisposable)
/// </summary>
public readonly struct PooledBuffer : IDisposable
{
    private readonly byte[] _buffer;
    private readonly bool _clearOnReturn;

    internal PooledBuffer(int minimumSize, bool clearOnReturn = false)
    {
        _buffer = SerializationBufferPool.Rent(minimumSize);
        _clearOnReturn = clearOnReturn;
    }

    /// <summary>
    /// Get the rented buffer
    /// </summary>
    public byte[] Buffer => _buffer;

    /// <summary>
    /// Get a span of the buffer
    /// </summary>
    public Span<byte> AsSpan() => _buffer.AsSpan();

    /// <summary>
    /// Get a span of the buffer (limited length)
    /// </summary>
    public Span<byte> AsSpan(int length) => _buffer.AsSpan(0, length);

    /// <summary>
    /// Return buffer to pool
    /// </summary>
    public void Dispose()
    {
        SerializationBufferPool.Return(_buffer, _clearOnReturn);
    }
}

/// <summary>
/// Array buffer writer for IBufferWriter&lt;byte&gt; support
/// Wraps ArrayPool for zero-allocation serialization
/// </summary>
public sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
{
    private byte[] _buffer;
    private int _position;

    public PooledBufferWriter(int initialCapacity = 1024)
    {
        _buffer = SerializationBufferPool.Rent(initialCapacity);
        _position = 0;
    }

    /// <summary>
    /// Written bytes count
    /// </summary>
    public int WrittenCount => _position;

    /// <summary>
    /// Get written data as ReadOnlySpan
    /// </summary>
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _position);

    /// <summary>
    /// Get written data as byte[] (copies data)
    /// </summary>
    public byte[] ToArray()
    {
        return _buffer.AsSpan(0, _position).ToArray();
    }

    /// <summary>
    /// Advance the writer position
    /// </summary>
    public void Advance(int count)
    {
        if (count < 0 || _position + count > _buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(count));

        _position += count;
    }

    /// <summary>
    /// Get memory to write to
    /// </summary>
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(_position);
    }

    /// <summary>
    /// Get span to write to
    /// </summary>
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(_position);
    }

    /// <summary>
    /// Clear the buffer (reset position)
    /// </summary>
    public void Clear()
    {
        _position = 0;
    }

    private void EnsureCapacity(int sizeHint)
    {
        var available = _buffer.Length - _position;
        if (available >= sizeHint)
            return;

        // Grow buffer (double or required size)
        var minCapacity = _position + sizeHint;
        var newCapacity = Math.Max(_buffer.Length * 2, minCapacity);

        var newBuffer = SerializationBufferPool.Rent(newCapacity);
        _buffer.AsSpan(0, _position).CopyTo(newBuffer);

        SerializationBufferPool.Return(_buffer);
        _buffer = newBuffer;
    }

    /// <summary>
    /// Return buffer to pool
    /// </summary>
    public void Dispose()
    {
        if (_buffer != null)
        {
            SerializationBufferPool.Return(_buffer);
            _buffer = null!;
        }
    }
}

