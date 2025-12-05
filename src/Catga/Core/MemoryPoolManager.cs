using System.Buffers;
using System.Runtime.CompilerServices;

namespace Catga.Core;

/// <summary>
/// Centralized memory pool manager for Catga (AOT-safe, thread-safe, zero-config)
/// </summary>
internal static class MemoryPoolManager
{
    /// <summary>
    /// Rent array from shared pool
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PooledArray<T> RentArray<T>(int minimumLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(minimumLength, 0, nameof(minimumLength));

        return new PooledArray<T>(ArrayPool<T>.Shared.Rent(minimumLength), minimumLength);
    }

    /// <summary>
    /// Rent buffer writer with specified initial capacity
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PooledBufferWriter<T> RentBufferWriter<T>(int initialCapacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(initialCapacity, 0, nameof(initialCapacity));

        return new PooledBufferWriter<T>(initialCapacity, ArrayPool<T>.Shared);
    }
}

/// <summary>
/// Readonly struct wrapper for pooled array with automatic disposal
/// </summary>
/// <typeparam name="T">The type of elements in the array</typeparam>
/// <remarks>
/// IMPORTANT: Must be disposed exactly once. Use 'using' statement to ensure proper cleanup.
/// Double-dispose is handled gracefully by ArrayPool but should be avoided for clarity.
/// <code>
/// // Correct usage:
/// using var buffer = MemoryPoolManager.RentArray&lt;byte&gt;(1024);
/// var span = buffer.Span;
/// // ... use span ...
/// // Automatically returned to pool when exiting scope
/// </code>
/// </remarks>
internal readonly struct PooledArray<T> : IDisposable
{
    /// <summary>
    /// The rented array
    /// </summary>
    public T[] Array { get; }

    /// <summary>
    /// The requested length (actual array may be larger)
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PooledArray{T}"/> struct
    /// </summary>
    public PooledArray(T[] array, int length)
    {
        Array = array ?? throw new ArgumentNullException(nameof(array));
        Length = length;
    }

    /// <summary>
    /// Get span of the requested length
    /// </summary>
    public Span<T> Span => Array.AsSpan(0, Length);

    /// <summary>
    /// Get memory of the requested length
    /// </summary>
    public Memory<T> Memory => Array.AsMemory(0, Length);

    /// <summary>
    /// Return array to pool
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() => ArrayPool<T>.Shared.Return(Array, clearArray: false);

    /// <summary>
    /// Implicitly convert to ReadOnlySpan
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ReadOnlySpan<T>(PooledArray<T> pooled) => pooled.Span;

    /// <summary>
    /// Implicitly convert to Span
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Span<T>(PooledArray<T> pooled) => pooled.Span;
}
