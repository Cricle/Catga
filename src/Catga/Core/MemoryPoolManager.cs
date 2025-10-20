using System.Buffers;
using System.Runtime.CompilerServices;

namespace Catga.Core;

/// <summary>
/// Centralized memory pool manager for Catga (AOT-safe, thread-safe, zero-config)
/// </summary>
public static class MemoryPoolManager
{
    /// <summary>
    /// Rent byte array from shared pool
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PooledArray RentArray(int minimumLength)
    {
        if (minimumLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumLength));

        return new PooledArray(ArrayPool<byte>.Shared.Rent(minimumLength), minimumLength);
    }

    /// <summary>
    /// Rent buffer writer with specified initial capacity
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PooledBufferWriter<byte> RentBufferWriter(int initialCapacity = 256)
    {
        if (initialCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));

        return new PooledBufferWriter<byte>(initialCapacity, ArrayPool<byte>.Shared);
    }
}

/// <summary>
/// Readonly struct wrapper for pooled array with automatic disposal
/// </summary>
/// <remarks>
/// IMPORTANT: Must be disposed exactly once. Use 'using' statement to ensure proper cleanup.
/// Double-dispose is handled gracefully by ArrayPool but should be avoided for clarity.
/// <code>
/// // Correct usage:
/// using var buffer = MemoryPoolManager.RentArray(1024);
/// var span = buffer.Span;
/// // ... use span ...
/// // Automatically returned to pool when exiting scope
/// </code>
/// </remarks>
public readonly struct PooledArray(byte[] array, int length) : IDisposable
{
    private readonly byte[] _array = array ?? throw new ArgumentNullException(nameof(array));
    private readonly int _length = length;

    /// <summary>
    /// The rented array
    /// </summary>
    public byte[] Array => _array;

    /// <summary>
    /// The requested length (actual array may be larger)
    /// </summary>
    public int Length => _length;

    /// <summary>
    /// Get span of the requested length
    /// </summary>
    public Span<byte> Span => _array.AsSpan(0, _length);

    /// <summary>
    /// Get memory of the requested length
    /// </summary>
    public Memory<byte> Memory => _array.AsMemory(0, _length);

    /// <summary>
    /// Return array to pool
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() => ArrayPool<byte>.Shared.Return(_array, clearArray: false);

    /// <summary>
    /// Implicitly convert to ReadOnlySpan
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ReadOnlySpan<byte>(PooledArray pooled) => pooled.Span;

    /// <summary>
    /// Implicitly convert to Span
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Span<byte>(PooledArray pooled) => pooled.Span;
}
