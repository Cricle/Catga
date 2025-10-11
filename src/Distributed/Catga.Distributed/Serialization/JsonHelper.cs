using System.Text.Json;

namespace Catga.Distributed.Serialization;

/// <summary>
/// JSON 序列化辅助类（AOT 兼容）
/// </summary>
public static class JsonHelper
{
    /// <summary>
    /// 序列化节点信息
    /// </summary>
    public static string SerializeNode(NodeInfo node)
        => JsonSerializer.Serialize(node, DistributedJsonContext.Default.NodeInfo);

    /// <summary>
    /// 反序列化节点信息
    /// </summary>
    public static NodeInfo? DeserializeNode(string json)
        => JsonSerializer.Deserialize(json, DistributedJsonContext.Default.NodeInfo);

    /// <summary>
    /// 序列化节点变更事件
    /// </summary>
    public static string SerializeNodeChangeEvent(NodeChangeEvent @event)
        => JsonSerializer.Serialize(@event, DistributedJsonContext.Default.NodeChangeEvent);

    /// <summary>
    /// 反序列化节点变更事件
    /// </summary>
    public static NodeChangeEvent? DeserializeNodeChangeEvent(string json)
        => JsonSerializer.Deserialize(json, DistributedJsonContext.Default.NodeChangeEvent);

    /// <summary>
    /// 序列化字典 (string, string)
    /// </summary>
    public static string SerializeDictionary(Dictionary<string, string> dict)
        => JsonSerializer.Serialize(dict, DistributedJsonContext.Default.DictionaryStringString);

    /// <summary>
    /// 反序列化字典 (string, string)
    /// </summary>
    public static Dictionary<string, string>? DeserializeDictionary(string json)
        => JsonSerializer.Deserialize(json, DistributedJsonContext.Default.DictionaryStringString);

    /// <summary>
    /// 序列化字典 (string, object)
    /// </summary>
    public static string SerializeDictionaryObject(Dictionary<string, object> dict)
        => JsonSerializer.Serialize(dict, DistributedJsonContext.Default.DictionaryStringObject);

    /// <summary>
    /// 反序列化字典 (string, object)
    /// </summary>
    public static Dictionary<string, object>? DeserializeDictionaryObject(string json)
        => JsonSerializer.Deserialize(json, DistributedJsonContext.Default.DictionaryStringObject);

    /// <summary>
    /// 序列化心跳信息
    /// </summary>
    public static string SerializeHeartbeat(string nodeId, double load, DateTime timestamp)
        => JsonSerializer.Serialize(new HeartbeatInfo(nodeId, load, timestamp), DistributedJsonContext.Default.HeartbeatInfo);

    /// <summary>
    /// 反序列化心跳信息
    /// </summary>
    public static HeartbeatInfo? DeserializeHeartbeat(string json)
        => JsonSerializer.Deserialize(json, DistributedJsonContext.Default.HeartbeatInfo);
}

