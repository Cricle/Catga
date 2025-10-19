using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Abstractions;
using Catga.Pooling;

namespace Catga.Serialization;

/// <summary>
/// Serialization helper with memory pooling and zero-allocation Base64 encoding
/// </summary>
/// <remarks>
/// Memory Optimizations:
/// - Uses ArrayPool for Base64 encoding/decoding
/// - Zero-allocation paths for small messages (stackalloc)
/// - Pooled buffer writers for large messages
/// - All methods are AOT-safe (generic only)
/// </remarks>
public static class SerializationHelper
{
    private const int StackAllocThreshold = 256; // Use stackalloc for < 256 bytes
    private const int SmallMessageThreshold = 1024; // Small message optimization

    /// <summary>
    /// Serialize object to Base64 string (pooled, zero-allocation for small messages)
    /// </summary>
    /// <remarks>AOT-safe when T is known at compile time</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T obj, IMessageSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);

        // Try to use pooled serialization if available
        if (serializer is IPooledMessageSerializer pooledSerializer)
        {
            using var pooledBuffer = pooledSerializer.SerializePooled(obj);
            return EncodeBase64Pooled(pooledBuffer.Memory.Span);
        }

        // Fallback to regular serialization
        var bytes = serializer.Serialize(obj);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Deserialize object from Base64 string (pooled, zero-allocation)
    /// </summary>
    /// <remarks>AOT-safe when T is known at compile time</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string data, IMessageSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        
        if (string.IsNullOrEmpty(data))
            return default;

        // Use pooled decoding
        using var decodedBytes = DecodeBase64Pooled(data);
        
        // Use Memory-based deserialization if available
        return serializer.Deserialize<T>(decodedBytes.Memory);
    }

    /// <summary>Try deserialize using provided serializer (required)</summary>
    public static bool TryDeserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string data, out T? result, IMessageSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        try
        {
            result = Deserialize<T>(data, serializer);
            return result != null;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    /// <summary>
    /// Encode bytes to Base64 string using ArrayPool (zero-allocation for small data)
    /// </summary>
    /// <remarks>
    /// AOT-safe. Uses stackalloc for small buffers, ArrayPool for large buffers.
    /// This is significantly faster than Convert.ToBase64String for repeated calls.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string EncodeBase64Pooled(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            return string.Empty;

        int base64Length = Base64.GetMaxEncodedToUtf8Length(data.Length);

        // Small data: use stackalloc
        if (base64Length <= StackAllocThreshold)
        {
            Span<byte> base64Buffer = stackalloc byte[base64Length];
            if (Base64.EncodeToUtf8(data, base64Buffer, out _, out int bytesWritten) == OperationStatus.Done)
            {
                return System.Text.Encoding.UTF8.GetString(base64Buffer.Slice(0, bytesWritten));
            }
        }

        // Large data: use ArrayPool
        var pool = MemoryPoolManager.Shared;
        var buffer = pool.RentArray(base64Length);
        try
        {
            if (Base64.EncodeToUtf8(data, buffer, out _, out int bytesWritten) == OperationStatus.Done)
            {
                return System.Text.Encoding.UTF8.GetString(buffer, 0, bytesWritten);
            }
            
            // Fallback
            return Convert.ToBase64String(data);
        }
        finally
        {
            pool.ReturnArray(buffer, clearArray: false);
        }
    }

    /// <summary>
    /// Decode Base64 string to bytes using ArrayPool (returns IMemoryOwner that must be disposed)
    /// </summary>
    /// <remarks>
    /// AOT-safe. Returns pooled memory that caller must dispose.
    /// Zero-allocation except for the IMemoryOwner instance.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IMemoryOwner<byte> DecodeBase64Pooled(string base64)
    {
        if (string.IsNullOrEmpty(base64))
            return new EmptyMemoryOwner();

        // Estimate decoded size (Base64 expands by ~33%)
        int maxDecodedLength = (base64.Length * 3) / 4;
        
        var pool = MemoryPoolManager.Shared;
        var owner = pool.RentMemory(maxDecodedLength);

        try
        {
            // Convert string to UTF8 bytes first
            Span<byte> utf8Bytes = stackalloc byte[base64.Length];
            int utf8ByteCount = System.Text.Encoding.UTF8.GetBytes(base64, utf8Bytes);

            // Decode Base64
            if (Base64.DecodeFromUtf8(utf8Bytes.Slice(0, utf8ByteCount), owner.Memory.Span, out _, out int bytesWritten) == OperationStatus.Done)
            {
                // Success: return memory owner (will be wrapped to limit length)
                return new SlicedMemoryOwner(owner, bytesWritten);
            }

            // Fallback: use Convert
            var decoded = Convert.FromBase64String(base64);
            decoded.CopyTo(owner.Memory.Span);
            return new SlicedMemoryOwner(owner, decoded.Length);
        }
        catch
        {
            owner.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Empty memory owner for empty strings (AOT-safe)
    /// </summary>
    private sealed class EmptyMemoryOwner : IMemoryOwner<byte>
    {
        public Memory<byte> Memory => Memory<byte>.Empty;
        public void Dispose() { }
    }

    /// <summary>
    /// Sliced memory owner that wraps another owner and limits the visible length (AOT-safe)
    /// </summary>
    private sealed class SlicedMemoryOwner : IMemoryOwner<byte>
    {
        private readonly IMemoryOwner<byte> _inner;
        private readonly int _length;

        public SlicedMemoryOwner(IMemoryOwner<byte> inner, int length)
        {
            _inner = inner;
            _length = length;
        }

        public Memory<byte> Memory => _inner.Memory.Slice(0, _length);

        public void Dispose() => _inner.Dispose();
    }
}
