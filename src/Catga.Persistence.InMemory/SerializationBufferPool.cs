using System.Buffers;
using System.Runtime.CompilerServices;

namespace Catga.Serialization;

/// <summary>Serialization buffer pool (lock-free, ArrayPool-based)</summary>
public static class SerializationBufferPool
{
    private static readonly ArrayPool<byte> _pool = ArrayPool<byte>.Shared;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] Rent(int minimumSize) => _pool.Rent(minimumSize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(byte[] buffer, bool clearArray = false) => _pool.Return(buffer, clearArray);

    public static PooledBuffer RentScoped(int minimumSize) => new PooledBuffer(minimumSize);
}

/// <summary>Pooled buffer with auto-return (IDisposable)</summary>
public readonly struct PooledBuffer : IDisposable
{
    private readonly byte[] _buffer;
    private readonly bool _clearOnReturn;

    internal PooledBuffer(int minimumSize, bool clearOnReturn = false)
    {
        _buffer = SerializationBufferPool.Rent(minimumSize);
        _clearOnReturn = clearOnReturn;
    }

    public byte[] Buffer => _buffer;
    public Span<byte> AsSpan() => _buffer.AsSpan();
    public Span<byte> AsSpan(int length) => _buffer.AsSpan(0, length);

    public void Dispose() => SerializationBufferPool.Return(_buffer, _clearOnReturn);
}

/// <summary>Array buffer writer for IBufferWriter (zero-allocation)</summary>
public sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
{
    private byte[] _buffer;
    private int _position;

    public PooledBufferWriter(int initialCapacity = 1024)
    {
        _buffer = SerializationBufferPool.Rent(initialCapacity);
        _position = 0;
    }

    public int WrittenCount => _position;
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _position);
    public byte[] ToArray() => _buffer.AsSpan(0, _position).ToArray();

    public void Advance(int count)
    {
        if (count < 0 || _position + count > _buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(count));
        _position += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(_position);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(_position);
    }

    public void Clear() => _position = 0;

    private void EnsureCapacity(int sizeHint)
    {
        var available = _buffer.Length - _position;
        if (available >= sizeHint) return;

        var minCapacity = _position + sizeHint;
        var newCapacity = Math.Max(_buffer.Length * 2, minCapacity);
        var newBuffer = SerializationBufferPool.Rent(newCapacity);
        _buffer.AsSpan(0, _position).CopyTo(newBuffer);
        SerializationBufferPool.Return(_buffer);
        _buffer = newBuffer;
    }

    public void Dispose()
    {
        if (_buffer != null)
        {
            SerializationBufferPool.Return(_buffer);
            _buffer = null!;
        }
    }
}

