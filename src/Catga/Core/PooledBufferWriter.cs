using System.Buffers;
using System.Runtime.CompilerServices;

namespace Catga.Core;

/// <summary>Recyclable buffer writer using ArrayPool (AOT-safe)</summary>
internal sealed class PooledBufferWriter<T> : IBufferWriter<T>, IDisposable
{
    private T[] _buffer;
    private int _index;
    private readonly ArrayPool<T> _pool;
    private bool _disposed;

    private const int MaxArrayLength = 0X7FEFFFFF;

    public PooledBufferWriter(int initialCapacity = 256, ArrayPool<T>? pool = null)
    {
        if (initialCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        _pool = pool ?? ArrayPool<T>.Shared;
        _buffer = _pool.Rent(initialCapacity);
    }

    public ReadOnlyMemory<T> WrittenMemory { get { ThrowIfDisposed(); return _buffer.AsMemory(0, _index); } }
    public ReadOnlySpan<T> WrittenSpan { get { ThrowIfDisposed(); return _buffer.AsSpan(0, _index); } }
    public int WrittenCount => _index;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        ThrowIfDisposed();
        if (_index > _buffer.Length - count) throw new InvalidOperationException("Cannot advance past buffer end");
        _index += count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Memory<T> GetMemory(int sizeHint = 0) { ThrowIfDisposed(); CheckAndResizeBuffer(sizeHint); return _buffer.AsMemory(_index); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> GetSpan(int sizeHint = 0) { ThrowIfDisposed(); CheckAndResizeBuffer(sizeHint); return _buffer.AsSpan(_index); }

    public void Clear()
    {
        ThrowIfDisposed();
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) _buffer.AsSpan(0, _index).Clear();
        _index = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) _buffer.AsSpan(0, _index).Clear();
        _pool.Return(_buffer, clearArray: false);
        _buffer = Array.Empty<T>();
        _index = 0;
        _disposed = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(PooledBufferWriter<T>)); }

    private void CheckAndResizeBuffer(int sizeHint)
    {
        var available = _buffer.Length - _index;
        if (sizeHint == 0 && available == 0) ResizeBuffer(_buffer.Length * 2);
        else if (sizeHint > available)
        {
            var newSize = Math.Max(_buffer.Length * 2, _index + sizeHint);
            if ((uint)newSize > MaxArrayLength)
            {
                newSize = MaxArrayLength;
                if (newSize - _index < sizeHint) throw new OutOfMemoryException("Buffer size would exceed maximum");
            }
            ResizeBuffer(newSize);
        }
    }

    private void ResizeBuffer(int newSize)
    {
        var newBuffer = _pool.Rent(newSize);
        _buffer.AsSpan(0, _index).CopyTo(newBuffer);
        _pool.Return(_buffer, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        _buffer = newBuffer;
    }
}