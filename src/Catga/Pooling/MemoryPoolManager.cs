using System.Buffers;
using System.Runtime.CompilerServices;

namespace Catga.Pooling;

/// <summary>
/// Centralized memory pool manager for Catga (AOT-safe, thread-safe, zero-config)
/// </summary>
/// <remarks>
/// Uses MemoryPool&lt;byte&gt;.Shared and ArrayPool&lt;byte&gt;.Shared for optimal performance.
/// All methods are thread-safe and AOT-compatible.
/// </remarks>
public static class MemoryPoolManager
{
    /// <summary>
    /// Rent memory from shared pool
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PooledMemory RentMemory(int minimumLength)
    {
        if (minimumLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumLength));

        return new PooledMemory(MemoryPool<byte>.Shared.Rent(minimumLength));
    }

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
/// Readonly struct wrapper for pooled memory with automatic disposal
/// </summary>
public readonly struct PooledMemory(IMemoryOwner<byte> owner) : IDisposable
{
    private readonly IMemoryOwner<byte> _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    /// <summary>
    /// The rented memory
    /// </summary>
    public Memory<byte> Memory => _owner.Memory;

    /// <summary>
    /// The actual length of the rented memory
    /// </summary>
    public int Length => _owner.Memory.Length;

    /// <summary>
    /// Return memory to pool
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() => _owner.Dispose();

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
    /// Wrap as IMemoryOwner for compatibility (creates small allocation)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal IMemoryOwner<byte> AsMemoryOwner() => new PooledMemoryWrapper(_owner);

    /// <summary>
    /// Minimal wrapper to expose IMemoryOwner
    /// </summary>
    private sealed class PooledMemoryWrapper(IMemoryOwner<byte> owner) : IMemoryOwner<byte>
    {
        public Memory<byte> Memory { get; } = owner.Memory;
        public void Dispose() => owner.Dispose();
    }
}

/// <summary>
/// Readonly struct wrapper for pooled array with automatic disposal
/// </summary>
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
