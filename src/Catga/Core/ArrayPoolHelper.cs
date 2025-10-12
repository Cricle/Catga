using System.Buffers;
using System.Runtime.CompilerServices;

namespace Catga.Common;

/// <summary>ArrayPool helper with automatic cleanup</summary>
public static class ArrayPoolHelper
{
    private const int DefaultThreshold = 16;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RentedArray<T> RentOrAllocate<T>(int count, int threshold = DefaultThreshold)
    {
        var canPool = count > threshold;
        var buffer = canPool ? ArrayPool<T>.Shared.Rent(count) : new T[count];
        return new RentedArray<T>(buffer, count, isRented: canPool);
    }
}

/// <summary>Wrapper for rented arrays with auto-cleanup (IDisposable)</summary>
public readonly struct RentedArray<T> : IDisposable
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

    public T[] Array => _array;
    public int Count => _count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> AsSpan() => _array.AsSpan(0, _count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Memory<T> AsMemory() => _array.AsMemory(0, _count);

    public void Dispose()
    {
        if (_isRented && _array != null)
        {
            System.Array.Clear(_array, 0, _count);
            ArrayPool<T>.Shared.Return(_array);
        }
    }
}

