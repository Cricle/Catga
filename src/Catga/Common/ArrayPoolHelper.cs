using System.Buffers;
using System.Runtime.CompilerServices;

namespace Catga.Common;

/// <summary>
/// ArrayPool helper for managing array rentals with automatic cleanup
/// Reduces code duplication and provides safe resource management
/// </summary>
internal static class ArrayPoolHelper
{
    private const int DefaultThreshold = 16;

    /// <summary>
    /// Rent array from pool or allocate new one based on size threshold
    /// </summary>
    /// <typeparam name="T">Array element type</typeparam>
    /// <param name="count">Required array size</param>
    /// <param name="threshold">Size threshold for using ArrayPool (default: 16)</param>
    /// <returns>RentedArray wrapper with automatic cleanup</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RentedArray<T> RentOrAllocate<T>(int count, int threshold = DefaultThreshold)
    {
        if (count > threshold)
        {
            var rentedArray = ArrayPool<T>.Shared.Rent(count);
            return new RentedArray<T>(rentedArray, count, isRented: true);
        }
        else
        {
            var array = new T[count];
            return new RentedArray<T>(array, count, isRented: false);
        }
    }
}

/// <summary>
/// Wrapper for rented or allocated arrays with automatic cleanup via IDisposable
/// Ensures arrays are properly returned to pool when done
/// </summary>
/// <typeparam name="T">Array element type</typeparam>
internal readonly struct RentedArray<T> : IDisposable
{
    private readonly T[] _array;
    private readonly int _count;
    private readonly bool _isRented;

    internal RentedArray(T[] array, int count, bool isRented)
    {
        _array = array;
        _count = count;
        _isRented = isRented;
    }

    /// <summary>
    /// Get the underlying array
    /// </summary>
    public T[] Array => _array;

    /// <summary>
    /// Get the actual count (may be less than Array.Length for rented arrays)
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Get a span of the actual used portion
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> AsSpan() => _array.AsSpan(0, _count);

    /// <summary>
    /// Get a memory of the actual used portion
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Memory<T> AsMemory() => _array.AsMemory(0, _count);

    /// <summary>
    /// Return array to pool if it was rented
    /// </summary>
    public void Dispose()
    {
        if (_isRented && _array != null)
        {
            System.Array.Clear(_array, 0, _count);
            ArrayPool<T>.Shared.Return(_array);
        }
    }
}

