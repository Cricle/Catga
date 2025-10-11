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
    public byte[] Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] T>(T value);

    /// <summary>
    /// Deserialize object from byte array
    /// </summary>
    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicConstructors)] T>(byte[] data);

    /// <summary>
    /// Serializer name
    /// </summary>
    public string Name { get; }
}

