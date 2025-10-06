using System.Diagnostics.CodeAnalysis;
using Catga.Serialization;
using MemoryPack;

namespace Catga.Serialization.MemoryPack;

/// <summary>
/// MemoryPack 消息序列化器 - 高性能二进制序列化（AOT 友好）
/// </summary>
public class MemoryPackMessageSerializer : IMessageSerializer
{
    public string Name => "MemoryPack";

    [RequiresUnreferencedCode("序列化可能需要无法静态分析的类型")]
    [RequiresDynamicCode("序列化可能需要运行时代码生成")]
    public byte[] Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] T>(T value)
    {
        return MemoryPackSerializer.Serialize(value);
    }

    [RequiresUnreferencedCode("反序列化可能需要无法静态分析的类型")]
    [RequiresDynamicCode("反序列化可能需要运行时代码生成")]
    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicConstructors)] T>(byte[] data)
    {
        return MemoryPackSerializer.Deserialize<T>(data);
    }
}

