using Catga.Abstractions;
using StackExchange.Redis;
using System.Runtime.CompilerServices;

namespace Catga.Persistence.Redis;

/// <summary>
/// Base class for Redis-based stores with common patterns (DRY principle)
/// </summary>
/// <remarks>
/// Provides:
/// - Unified Redis connection and serializer management
/// - Optimized GetDatabase() access
/// - Key building utilities with Span optimization
/// - Common patterns for all Redis stores
/// </remarks>
public abstract class RedisStoreBase
{
    /// <summary>
    /// Redis connection multiplexer (managed by DI container)
    /// </summary>
    protected readonly IConnectionMultiplexer Redis;

    /// <summary>
    /// Message serializer for AOT-compatible serialization
    /// </summary>
    protected readonly IMessageSerializer Serializer;

    /// <summary>
    /// Key prefix for this store (e.g., "idempotency:", "dlq:")
    /// </summary>
    protected readonly string KeyPrefix;

    /// <summary>
    /// Initialize base Redis store
    /// </summary>
    protected RedisStoreBase(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        string keyPrefix)
    {
        Redis = redis ?? throw new ArgumentNullException(nameof(redis));
        Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        KeyPrefix = keyPrefix ?? throw new ArgumentNullException(nameof(keyPrefix));
    }

    /// <summary>
    /// Get Redis database (inline for performance)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected IDatabase GetDatabase() => Redis.GetDatabase();

    /// <summary>
    /// Build key with prefix and string suffix (stack-allocated for small keys)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected string BuildKey(string suffix)
    {
        if (string.IsNullOrEmpty(suffix))
            return KeyPrefix;

        var totalLen = KeyPrefix.Length + suffix.Length;
        if (totalLen <= 256)
        {
            Span<char> buffer = stackalloc char[256];
            KeyPrefix.AsSpan().CopyTo(buffer);
            suffix.AsSpan().CopyTo(buffer[KeyPrefix.Length..]);
            return new string(buffer[..totalLen]);
        }

        // Fallback for large keys
        return $"{KeyPrefix}{suffix}";
    }

    /// <summary>
    /// Build key with prefix and long ID (stack-allocated, optimized)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected string BuildKey(long id)
    {
        // For long IDs, convert to string with stack allocation
        Span<char> idBuffer = stackalloc char[20];  // long max = 19 digits + sign
        id.TryFormat(idBuffer, out var idLen);

        if (KeyPrefix.Length + idLen <= 256)
        {
            Span<char> buffer = stackalloc char[256];
            KeyPrefix.AsSpan().CopyTo(buffer);
            idBuffer[..idLen].CopyTo(buffer[KeyPrefix.Length..]);
            return new string(buffer[..(KeyPrefix.Length + idLen)]);
        }

        // Fallback for large keys
        return $"{KeyPrefix}{id}";
    }

    /// <summary>
    /// Build key with prefix and Guid (stack-allocated)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected string BuildKey(Guid id)
    {
        // Guid.ToString() is "N" format: 32 chars
        Span<char> guidBuffer = stackalloc char[32];
        id.TryFormat(guidBuffer, out var guidLen, "N");

        if (KeyPrefix.Length + guidLen <= 256)
        {
            Span<char> buffer = stackalloc char[256];
            KeyPrefix.AsSpan().CopyTo(buffer);
            guidBuffer[..guidLen].CopyTo(buffer[KeyPrefix.Length..]);
            return new string(buffer[..(KeyPrefix.Length + guidLen)]);
        }

        // Fallback
        return $"{KeyPrefix}{id:N}";
    }
}

