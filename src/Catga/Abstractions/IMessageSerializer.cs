using System.Diagnostics.CodeAnalysis;

namespace Catga.Abstractions;

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
    /// Serialize object to byte array
    /// </summary>
    public byte[] Serialize(object? value, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type);

    /// <summary>
    /// Deserialize object from byte array
    /// </summary>
    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(byte[] data);

    /// <summary>
    /// Deserialize object from byte array
    /// </summary>
    public object? Deserialize(byte[] data, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type);

    /// <summary>
    /// Serializer name
    /// </summary>
    public string Name { get; }
}

