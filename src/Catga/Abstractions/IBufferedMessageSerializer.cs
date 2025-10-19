using Catga.Abstractions;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Catga.Serialization;

/// <summary>
/// Buffered message serializer with advanced pooling and zero-copy support
/// </summary>
/// <remarks>
/// Reduces allocations by using ArrayPool, IBufferWriter, and Span/Memory APIs.
/// All generic methods are AOT-friendly when T is known at compile time.
/// </remarks>
public interface IBufferedMessageSerializer : IMessageSerializer
{
    /// <summary>
    /// Serialize to buffer writer (zero-copy, no allocation)
    /// </summary>
    /// <remarks>AOT-friendly. Recommended for high-performance scenarios.</remarks>
    void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        T value,
        IBufferWriter<byte> bufferWriter);

    /// <summary>
    /// Serialize to buffer writer (non-generic, uses reflection)
    /// </summary>
    /// <remarks>Not AOT-compatible. Use generic version for AOT.</remarks>
    void Serialize(
        object? value,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type,
        IBufferWriter<byte> bufferWriter);

    /// <summary>
    /// Deserialize from ReadOnlySpan (zero-copy, no allocation)
    /// </summary>
    /// <remarks>AOT-friendly. Best performance for sync scenarios.</remarks>
    T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        ReadOnlySpan<byte> data);

    /// <summary>
    /// Deserialize from ReadOnlySpan (non-generic, uses reflection)
    /// </summary>
    /// <remarks>Not AOT-compatible. Use generic version for AOT.</remarks>
    object? Deserialize(
        ReadOnlySpan<byte> data,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type);

    /// <summary>
    /// Try serialize to Span (stackalloc-friendly, zero allocation)
    /// </summary>
    /// <remarks>
    /// AOT-friendly. Ideal for small messages with stackalloc.
    /// Returns false if destination is too small.
    /// </remarks>
    bool TrySerialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        T value,
        Span<byte> destination,
        out int bytesWritten);

    /// <summary>
    /// Serialize to Memory (for async scenarios, zero-copy)
    /// </summary>
    /// <remarks>AOT-friendly. Async-friendly alternative to Span.</remarks>
    void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        T value,
        Memory<byte> destination,
        out int bytesWritten);

    /// <summary>
    /// Serialize batch of items to buffer writer
    /// </summary>
    /// <remarks>
    /// AOT-friendly. Optimized for batch serialization with length prefix.
    /// Returns total bytes written.
    /// </remarks>
    int SerializeBatch<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        IEnumerable<T> values,
        IBufferWriter<byte> bufferWriter);

    /// <summary>
    /// Get serialized size estimate (for pre-allocation)
    /// </summary>
    /// <remarks>AOT-friendly. Returns conservative estimate.</remarks>
    int GetSizeEstimate<T>(T value);
}

