using System.Buffers;
using System.Runtime.CompilerServices;
using Catga;

namespace Catga.Core;

/// <summary>
/// Recyclable buffer writer using ArrayPool (AOT-safe, zero reflection)
/// </summary>
/// <typeparam name="T">Element type</typeparam>
/// <remarks>
/// <para>
/// IBufferWriter with automatic buffer growth and pooling.
/// Buffers are rented from ArrayPool and returned on Dispose.
/// </para>
/// <para>
/// AOT Compatibility:
/// - No reflection or dynamic code generation
/// - Uses only ArrayPool (AOT-safe)
/// - All methods are trim-safe
/// </para>
/// <para>
/// Thread Safety: Not thread-safe. Each instance should be used by single thread.
/// </para>
/// </remarks>
public sealed class PooledBufferWriter<T> : IBufferWriter<T>, IDisposable
{
    private T[] _buffer;
    private int _index;
    private readonly ArrayPool<T> _pool;
    private bool _disposed;

    private const int DefaultInitialCapacity = 256;
    private const int MaxArrayLength = 0X7FEFFFFF; // Array.MaxLength

    /// <summary>
    /// Create pooled buffer writer with specified capacity
    /// </summary>
    /// <param name="initialCapacity">Initial capacity (default: 256)</param>
    /// <param name="pool">Array pool to use (default: ArrayPool.Shared)</param>
    public PooledBufferWriter(int initialCapacity = DefaultInitialCapacity, ArrayPool<T>? pool = null)
    {
        if (initialCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(initialCapacity), "Initial capacity must be positive");

        _pool = pool ?? ArrayPool<T>.Shared;
        _buffer = _pool.Rent(initialCapacity);
        _index = 0;
        _disposed = false;
    }

    /// <inheritdoc />
    public ReadOnlyMemory<T> WrittenMemory
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ThrowIfDisposed();
            return _buffer.AsMemory(0, _index);
        }
    }

    /// <inheritdoc />
    public ReadOnlySpan<T> WrittenSpan
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ThrowIfDisposed();
            return _buffer.AsSpan(0, _index);
        }
    }

    /// <inheritdoc />
    public int WrittenCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _index;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be non-negative");

        ThrowIfDisposed();

        if (_index > _buffer.Length - count)
            throw new InvalidOperationException("Cannot advance past the end of the buffer");

        _index += count;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Memory<T> GetMemory(int sizeHint = 0)
    {
        ThrowIfDisposed();
        CheckAndResizeBuffer(sizeHint);
        return _buffer.AsMemory(_index);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> GetSpan(int sizeHint = 0)
    {
        ThrowIfDisposed();
        CheckAndResizeBuffer(sizeHint);
        return _buffer.AsSpan(_index);
    }

    /// <inheritdoc />
    public void Clear()
    {
        ThrowIfDisposed();

        // Clear sensitive data if T is not a value type
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            _buffer.AsSpan(0, _index).Clear();
        }

        _index = 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        // Clear sensitive data before returning to pool
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            _buffer.AsSpan(0, _index).Clear();
        }

        _pool.Return(_buffer, clearArray: false);
        _buffer = Array.Empty<T>();
        _index = 0;
        _disposed = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PooledBufferWriter<T>));
    }

    private void CheckAndResizeBuffer(int sizeHint)
    {
        int availableSpace = _buffer.Length - _index;

        if (sizeHint == 0)
        {
            // No hint provided, ensure at least 1 byte available
            if (availableSpace == 0)
            {
                ResizeBuffer(_buffer.Length * 2);
            }
        }
        else if (sizeHint > availableSpace)
        {
            // Need more space
            int newSize = Math.Max(_buffer.Length * 2, _index + sizeHint);

            // Check for overflow
            if ((uint)newSize > MaxArrayLength)
            {
                newSize = MaxArrayLength;
                if (newSize - _index < sizeHint)
                {
                    throw new OutOfMemoryException("Buffer size would exceed maximum array length");
                }
            }

            ResizeBuffer(newSize);
        }
    }

    private void ResizeBuffer(int newSize)
    {
        var newBuffer = _pool.Rent(newSize);

        // Copy existing data
        _buffer.AsSpan(0, _index).CopyTo(newBuffer);

        // Return old buffer
        _pool.Return(_buffer, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());

        _buffer = newBuffer;
    }
}

