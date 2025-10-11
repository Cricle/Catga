using System.Diagnostics.CodeAnalysis;

namespace Catga.Serialization;

/// <summary>
/// Message serializer interface (AOT-friendly)
/// </summary>
public interface IMessageSerializer
{
    /// <summary>
    /// Serialize object to byte array
    /// </summary>
    [RequiresUnreferencedCode("Serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Serialization may require runtime code generation")]
    public byte[] Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] T>(T value);

    /// <summary>
    /// Deserialize object from byte array
    /// </summary>
    [RequiresUnreferencedCode("Deserialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Deserialization may require runtime code generation")]
    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicConstructors)] T>(byte[] data);

    /// <summary>
    /// Serializer name
    /// </summary>
    public string Name { get; }
}

