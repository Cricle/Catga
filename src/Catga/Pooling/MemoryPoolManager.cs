using System.Buffers;
using System.Runtime.CompilerServices;

namespace Catga.Pooling;

/// <summary>
/// Centralized memory pool manager for Catga (AOT-safe, thread-safe)
/// </summary>
/// <remarks>
/// Uses MemoryPool&lt;byte&gt;.Shared for optimal performance and zero configuration.
/// Thread-safe and AOT-compatible.
/// </remarks>
public static class MemoryPoolManager
{
    /// <summary>
    /// Rent memory from shared pool
    /// </summary>
    /// <param name="minimumLength">Minimum length required</param>
    /// <returns>Pooled memory handle (must dispose)</returns>
    /// <remarks>AOT-safe. Uses MemoryPool&lt;byte&gt;.Shared.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PooledMemory RentMemory(int minimumLength)
    {
        if (minimumLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumLength), "Minimum length must be positive");

        return new PooledMemory(MemoryPool<byte>.Shared.Rent(minimumLength));
    }

    /// <summary>
    /// Rent byte array from shared pool
    /// </summary>
    /// <param name="minimumLength">Minimum length required</param>
    /// <returns>Rented array handle (must dispose)</returns>
    /// <remarks>AOT-safe. Uses ArrayPool&lt;byte&gt;.Shared.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PooledArray RentArray(int minimumLength)
    {
        if (minimumLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumLength), "Minimum length must be positive");

        return new PooledArray(ArrayPool<byte>.Shared.Rent(minimumLength), minimumLength);
    }

    /// <summary>
    /// Rent buffer writer with specified initial capacity
    /// </summary>
    /// <param name="initialCapacity">Initial capacity</param>
    /// <returns>Pooled buffer writer (must dispose)</returns>
    /// <remarks>AOT-safe. Uses ArrayPool&lt;byte&gt;.Shared.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PooledBufferWriter<byte> RentBufferWriter(int initialCapacity = 256)
    {
        if (initialCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(initialCapacity), "Initial capacity must be positive");

        return new PooledBufferWriter<byte>(initialCapacity, ArrayPool<byte>.Shared);
    }
}

/// <summary>
/// Readonly struct wrapper for pooled memory with automatic disposal
/// </summary>
/// <remarks>
/// AOT-safe. Always use with 'using' statement to ensure proper disposal.
/// </remarks>
public readonly struct PooledMemory : IDisposable
{
    private readonly IMemoryOwner<byte>? _owner;

    /// <summary>
    /// The rented memory
    /// </summary>
    public Memory<byte> Memory => _owner?.Memory ?? Memory<byte>.Empty;

    /// <summary>
    /// The actual length of the rented memory
    /// </summary>
    public int Length => Memory.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal PooledMemory(IMemoryOwner<byte> owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <summary>
    /// Return memory to pool
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        _owner?.Dispose();
    }

    /// <summary>
    /// Convert to IMemoryOwner for compatibility
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IMemoryOwner<byte> ToMemoryOwner()
    {
        return new PooledMemoryOwner(this);
    }

    /// <summary>
    /// Implicitly convert to ReadOnlyMemory
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ReadOnlyMemory<byte>(PooledMemory pooled) => pooled.Memory;

    /// <summary>
    /// Implicitly convert to Memory
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Memory<byte>(PooledMemory pooled) => pooled.Memory;

    /// <summary>
    /// Wrapper class to expose PooledMemory as IMemoryOwner
    /// </summary>
    private sealed class PooledMemoryOwner : IMemoryOwner<byte>
    {
        private PooledMemory _pooled;

        public PooledMemoryOwner(PooledMemory pooled) => _pooled = pooled;

        public Memory<byte> Memory => _pooled.Memory;

        public void Dispose() => _pooled.Dispose();
    }
}

/// <summary>
/// Readonly struct wrapper for pooled array with automatic disposal
/// </summary>
/// <remarks>
/// AOT-safe. Always use with 'using' statement to ensure proper disposal.
/// </remarks>
public readonly struct PooledArray : IDisposable
{
    private readonly byte[]? _array;
    private readonly int _length;

    /// <summary>
    /// The rented array
    /// </summary>
    public byte[] Array => _array ?? System.Array.Empty<byte>();

    /// <summary>
    /// The requested length (actual array may be larger)
    /// </summary>
    public int Length => _length;

    /// <summary>
    /// Get span of the requested length
    /// </summary>
    public Span<byte> Span => _array != null ? _array.AsSpan(0, _length) : Span<byte>.Empty;

    /// <summary>
    /// Get memory of the requested length
    /// </summary>
    public Memory<byte> Memory => _array != null ? _array.AsMemory(0, _length) : Memory<byte>.Empty;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal PooledArray(byte[] array, int length)
    {
        _array = array ?? throw new ArgumentNullException(nameof(array));
        _length = length;
    }

    /// <summary>
    /// Return array to pool
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (_array != null)
            ArrayPool<byte>.Shared.Return(_array, clearArray: false);
    }

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

