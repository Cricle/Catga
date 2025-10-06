using Catga.Serialization;
using MemoryPack;

namespace Catga.Serialization.MemoryPack;

/// <summary>
/// MemoryPack 消息序列化器 - 高性能二进制序列化（AOT 友好）
/// </summary>
public class MemoryPackMessageSerializer : IMessageSerializer
{
    public string Name => "MemoryPack";

    public byte[] Serialize<T>(T value)
    {
        return MemoryPackSerializer.Serialize(value);
    }

    public T? Deserialize<T>(byte[] data)
    {
        return MemoryPackSerializer.Deserialize<T>(data);
    }
}

