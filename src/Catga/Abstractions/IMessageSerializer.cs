using System.Diagnostics.CodeAnalysis;

namespace Catga.Serialization;

/// <summary>
/// Message serializer interface
/// Note: For AOT compatibility, use MemoryPack or provide JsonSerializerContext
/// </summary>
public interface IMessageSerializer
{
    /// <summary>
    /// Serialize object to byte array
    /// </summary>
    public byte[] Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value);

    /// <summary>
    /// Deserialize object from byte array
    /// </summary>
    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(byte[] data);

    /// <summary>
    /// Serializer name
    /// </summary>
    public string Name { get; }
}

