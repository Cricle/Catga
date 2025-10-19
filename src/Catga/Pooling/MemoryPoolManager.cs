using System.Buffers;
using System.Runtime.CompilerServices;
using Catga.Serialization;

namespace Catga.Pooling;

/// <summary>
/// Centralized memory pool manager for Catga (AOT-safe, thread-safe)
/// </summary>
/// <remarks>
/// <para>
/// Provides unified memory management across all Catga components.
/// Uses tiered pooling strategy for optimal performance.
/// </para>
/// <para>
/// AOT Compatibility:
/// - No reflection or dynamic code generation
/// - Uses only built-in ArrayPool and MemoryPool
/// - All pools are created at construction time
/// </para>
/// <para>
/// Thread Safety: All methods are thread-safe
/// </para>
/// </remarks>
public sealed class MemoryPoolManager : IDisposable
{
    private static readonly Lazy<MemoryPoolManager> _shared = new(() => new MemoryPoolManager());
    
    private readonly ArrayPool<byte> _smallBytePool;   // < 4KB
    private readonly ArrayPool<byte> _mediumBytePool;  // 4KB - 64KB  
    private readonly ArrayPool<byte> _largeBytePool;   // > 64KB
    private readonly MemoryPool<byte> _memoryPool;
    
    private bool _disposed;

    // Size thresholds
    private const int SmallSizeThreshold = 4 * 1024;        // 4KB
    private const int MediumSizeThreshold = 64 * 1024;      // 64KB
    
    // Pool configurations
    private const int SmallPoolMaxArrayLength = 16 * 1024;     // 16KB max for small pool
    private const int MediumPoolMaxArrayLength = 128 * 1024;   // 128KB max for medium pool

    /// <summary>
    /// Get shared instance (singleton, thread-safe)
    /// </summary>
    public static MemoryPoolManager Shared => _shared.Value;

    /// <summary>
    /// Create new instance with default configuration
    /// </summary>
    public MemoryPoolManager()
    {
        _smallBytePool = ArrayPool<byte>.Create(SmallPoolMaxArrayLength, 50);
        _mediumBytePool = ArrayPool<byte>.Create(MediumPoolMaxArrayLength, 20);
        _largeBytePool = ArrayPool<byte>.Shared;  // Use shared pool for large arrays
        _memoryPool = MemoryPool<byte>.Shared;
    }

    /// <summary>
    /// Get appropriate byte array pool based on size
    /// </summary>
    /// <remarks>AOT-safe</remarks>
    public ArrayPool<byte> SmallBytePool => _smallBytePool;

    /// <summary>
    /// Get medium byte array pool (4KB - 64KB)
    /// </summary>
    /// <remarks>AOT-safe</remarks>
    public ArrayPool<byte> MediumBytePool => _mediumBytePool;

    /// <summary>
    /// Get large byte array pool (> 64KB)
    /// </summary>
    /// <remarks>AOT-safe</remarks>
    public ArrayPool<byte> LargeBytePool => _largeBytePool;

    /// <summary>
    /// Rent buffer writer with specified initial capacity
    /// </summary>
    /// <param name="initialCapacity">Initial capacity</param>
    /// <returns>Pooled buffer writer (must dispose)</returns>
    /// <remarks>AOT-safe. Automatically selects appropriate pool.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PooledBufferWriter<byte> RentBufferWriter(int initialCapacity = 256)
    {
        ThrowIfDisposed();
        var pool = SelectPool(initialCapacity);
        return new PooledBufferWriter<byte>(initialCapacity, pool);
    }

    /// <summary>
    /// Return buffer writer to pool (manual, prefer using)
    /// </summary>
    /// <param name="writer">Writer to return</param>
    /// <remarks>AOT-safe. Prefer using statement for automatic disposal.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return(PooledBufferWriter<byte> writer)
    {
        writer?.Dispose();
    }

    /// <summary>
    /// Rent memory from pool
    /// </summary>
    /// <param name="minimumLength">Minimum length required</param>
    /// <returns>Memory owner (must dispose)</returns>
    /// <remarks>AOT-safe. Uses MemoryPool for efficient allocation.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IMemoryOwner<byte> RentMemory(int minimumLength)
    {
        ThrowIfDisposed();
        
        if (minimumLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumLength), "Minimum length must be positive");

        return _memoryPool.Rent(minimumLength);
    }

    /// <summary>
    /// Rent byte array from pool
    /// </summary>
    /// <param name="minimumLength">Minimum length required</param>
    /// <returns>Rented array (must return manually)</returns>
    /// <remarks>AOT-safe. Automatically selects appropriate pool.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] RentArray(int minimumLength)
    {
        ThrowIfDisposed();
        
        if (minimumLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumLength), "Minimum length must be positive");

        var pool = SelectPool(minimumLength);
        return pool.Rent(minimumLength);
    }

    /// <summary>
    /// Return byte array to pool
    /// </summary>
    /// <param name="array">Array to return</param>
    /// <param name="clearArray">Whether to clear the array</param>
    /// <remarks>AOT-safe</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReturnArray(byte[] array, bool clearArray = false)
    {
        if (array == null || array.Length == 0)
            return;

        var pool = SelectPool(array.Length);
        pool.Return(array, clearArray);
    }

    /// <summary>
    /// Get memory pool statistics
    /// </summary>
    /// <returns>Statistics snapshot</returns>
    /// <remarks>AOT-safe. For monitoring and diagnostics.</remarks>
    public MemoryPoolStatistics GetStatistics()
    {
        ThrowIfDisposed();
        
        // Note: ArrayPool doesn't expose detailed statistics
        // This is a placeholder for future enhancement
        return new MemoryPoolStatistics
        {
            SmallPoolConfiguredSize = SmallPoolMaxArrayLength,
            MediumPoolConfiguredSize = MediumPoolMaxArrayLength,
            IsShared = false
        };
    }

    /// <summary>
    /// Dispose and release all pooled resources
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        // Note: ArrayPool.Shared should not be disposed
        // Custom pools would need disposal if we held them

        _disposed = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MemoryPoolManager));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ArrayPool<byte> SelectPool(int size)
    {
        if (size < SmallSizeThreshold)
            return _smallBytePool;
        else if (size < MediumSizeThreshold)
            return _mediumBytePool;
        else
            return _largeBytePool;
    }
}

/// <summary>
/// Memory pool statistics (AOT-safe)
/// </summary>
public readonly struct MemoryPoolStatistics
{
    /// <summary>
    /// Small pool configured max size
    /// </summary>
    public int SmallPoolConfiguredSize { get; init; }

    /// <summary>
    /// Medium pool configured max size
    /// </summary>
    public int MediumPoolConfiguredSize { get; init; }

    /// <summary>
    /// Whether using shared pools
    /// </summary>
    public bool IsShared { get; init; }
}

