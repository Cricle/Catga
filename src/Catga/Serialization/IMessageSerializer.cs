namespace Catga.Serialization;

/// <summary>
/// 消息序列化器接口（AOT 友好）
/// </summary>
public interface IMessageSerializer
{
    /// <summary>
    /// 序列化对象为字节数组
    /// </summary>
    byte[] Serialize<T>(T value);

    /// <summary>
    /// 从字节数组反序列化对象
    /// </summary>
    T? Deserialize<T>(byte[] data);

    /// <summary>
    /// 序列化器名称
    /// </summary>
    string Name { get; }
}

