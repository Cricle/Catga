using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Catga.Abstractions;

/// <summary>
/// Message serializer interface (AOT-safe, minimal API)
/// </summary>
public interface IMessageSerializer
{
    /// <summary>
    /// Serializer name
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Serialize to byte array
    /// </summary>
    [RequiresDynamicCode("Serialization may use reflection for certain types")]
    [RequiresUnreferencedCode("Serialization may require unreferenced code for certain types")]
    byte[] Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value);

    /// <summary>
    /// Deserialize from byte array
    /// </summary>
    [RequiresDynamicCode("Deserialization may use reflection for certain types")]
    [RequiresUnreferencedCode("Deserialization may require unreferenced code for certain types")]
    T Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(byte[] data);

    /// <summary>
    /// Deserialize from ReadOnlySpan (zero-copy)
    /// </summary>
    [RequiresDynamicCode("Deserialization may use reflection for certain types")]
    [RequiresUnreferencedCode("Deserialization may require unreferenced code for certain types")]
    T Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(ReadOnlySpan<byte> data);

    /// <summary>
    /// Serialize to buffer writer (zero-allocation)
    /// </summary>
    [RequiresDynamicCode("Serialization may use reflection for certain types")]
    [RequiresUnreferencedCode("Serialization may require unreferenced code for certain types")]
    void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value, IBufferWriter<byte> bufferWriter);
}
