using System.Diagnostics.CodeAnalysis;

namespace Catga.Serialization;

/// <summary>
/// 消息序列化器接口（AOT 友好）
/// </summary>
public interface IMessageSerializer
{
    /// <summary>
    /// 序列化对象为字节数组
    /// </summary>
    [RequiresUnreferencedCode("序列化可能需要无法静态分析的类型")]
    [RequiresDynamicCode("序列化可能需要运行时代码生成")]
    byte[] Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] T>(T value);

    /// <summary>
    /// 从字节数组反序列化对象
    /// </summary>
    [RequiresUnreferencedCode("反序列化可能需要无法静态分析的类型")]
    [RequiresDynamicCode("反序列化可能需要运行时代码生成")]
    T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicConstructors)] T>(byte[] data);

    /// <summary>
    /// 序列化器名称
    /// </summary>
    string Name { get; }
}

