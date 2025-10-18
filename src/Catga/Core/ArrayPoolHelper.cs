using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace Catga.Common;

/// <summary>ArrayPool helper with automatic cleanup and encoding utilities</summary>
public static class ArrayPoolHelper
{
    private const int DefaultThreshold = 16;
    private static readonly Encoding Utf8Encoding = Encoding.UTF8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RentedArray<T> RentOrAllocate<T>(int count, int threshold = DefaultThreshold)
    {
        var canPool = count > threshold;
        var buffer = canPool ? ArrayPool<T>.Shared.Rent(count) : new T[count];
        return new RentedArray<T>(buffer, count, isRented: canPool);
    }

    #region Encoding Utilities (Zero-allocation conversions)

    /// <summary>
    /// Convert byte[] to string (zero-allocation for small buffers)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetString(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return string.Empty;

        return Utf8Encoding.GetString(bytes);
    }

    /// <summary>
    /// Convert byte[] to string using Span (zero-allocation)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetString(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return string.Empty;

        return Utf8Encoding.GetString(bytes);
    }

    /// <summary>
    /// Convert string to byte[] (allocates new array)
    /// For scenarios where caller needs ownership of the array
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] GetBytes(string str)
    {
        if (string.IsNullOrEmpty(str))
            return Array.Empty<byte>();

        return Utf8Encoding.GetBytes(str);
    }

    /// <summary>
    /// Convert string to byte[] using provided buffer
    /// Returns the actual byte count written
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetBytes(string str, Span<byte> destination)
    {
        if (string.IsNullOrEmpty(str))
            return 0;

        return Utf8Encoding.GetBytes(str, destination);
    }

    /// <summary>
    /// Convert byte[] to Base64 string (uses internal pooling)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToBase64String(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return string.Empty;

        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Convert byte[] to Base64 string using Span (zero-allocation)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToBase64String(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return string.Empty;

        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Convert Base64 string to byte[] (allocates new array)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] FromBase64String(string base64)
    {
        if (string.IsNullOrEmpty(base64))
            return Array.Empty<byte>();

        return Convert.FromBase64String(base64);
    }

    /// <summary>
    /// Try convert Base64 string to byte[] using provided buffer
    /// Returns true if successful and sets bytesWritten
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryFromBase64String(string base64, Span<byte> destination, out int bytesWritten)
    {
        if (string.IsNullOrEmpty(base64))
        {
            bytesWritten = 0;
            return true;
        }

        return Convert.TryFromBase64String(base64, destination, out bytesWritten);
    }

    #endregion
}

/// <summary>Wrapper for rented arrays with auto-cleanup (IDisposable)</summary>
public struct RentedArray<T> : IDisposable
{
    private readonly T[] _array;
    private readonly int _count;
    private readonly bool _isRented;
    private bool _detached;

    internal RentedArray(T[] array, int count, bool isRented)
    {
        _array = array;
        _count = count;
        _isRented = isRented;
        _detached = false;
    }

    public T[] Array => _array;
    public int Count => _count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> AsSpan() => _array.AsSpan(0, _count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Memory<T> AsMemory() => _array.AsMemory(0, _count);

    /// <summary>
    /// Detach array from pool management, preventing return on Dispose.
    /// Caller takes ownership of the array.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T[] Detach()
    {
        _detached = true;
        return _array;
    }

    public void Dispose()
    {
        if (_isRented && !_detached && _array != null)
        {
            System.Array.Clear(_array, 0, _count);
            ArrayPool<T>.Shared.Return(_array);
        }
    }
}

