using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Catga.Serialization;

/// <summary>
/// Buffered message serializer interface with pooling support
/// Reduces allocations by using ArrayPool and IBufferWriter
/// </summary>
public interface IBufferedMessageSerializer : IMessageSerializer
{
    /// <summary>
    /// Serialize to buffer writer (zero-copy, no allocation)
    /// </summary>
    [RequiresUnreferencedCode("Serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Serialization may require runtime code generation")]
    public void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] T>(
        T value,
        IBufferWriter<byte> bufferWriter);

    /// <summary>
    /// Deserialize from ReadOnlySpan (zero-copy, no allocation)
    /// </summary>
    [RequiresUnreferencedCode("Deserialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Deserialization may require runtime code generation")]
    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        ReadOnlySpan<byte> data);

    /// <summary>
    /// Get serialized size estimate (for pre-allocation)
    /// </summary>
    public int GetSizeEstimate<T>(T value);
}

