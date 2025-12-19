using System.Buffers;
using System.Runtime.CompilerServices;

namespace Catga.Core;

/// <summary>Centralized memory pool manager (AOT-safe, thread-safe)</summary>
internal static class MemoryPoolManager
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PooledArray<T> RentArray<T>(int minimumLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(minimumLength, 0);
        return new PooledArray<T>(ArrayPool<T>.Shared.Rent(minimumLength), minimumLength);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PooledBufferWriter<T> RentBufferWriter<T>(int initialCapacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(initialCapacity, 0);
        return new PooledBufferWriter<T>(initialCapacity, ArrayPool<T>.Shared);
    }
}

/// <summary>Pooled array wrapper with automatic disposal</summary>
internal readonly struct PooledArray<T> : IDisposable
{
    public T[] Array { get; }
    public int Length { get; }

    public PooledArray(T[] array, int length)
    {
        Array = array ?? throw new ArgumentNullException(nameof(array));
        Length = length;
    }

    public Span<T> Span => Array.AsSpan(0, Length);
    public Memory<T> Memory => Array.AsMemory(0, Length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() => ArrayPool<T>.Shared.Return(Array, clearArray: false);

    public static implicit operator ReadOnlySpan<T>(PooledArray<T> pooled) => pooled.Span;
    public static implicit operator Span<T>(PooledArray<T> pooled) => pooled.Span;
}
