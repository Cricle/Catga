using System.Text.Json;
using System.Text.Json.Serialization;

namespace Catga.Distributed.Serialization;

/// <summary>
/// 心跳信息
/// </summary>
public record HeartbeatInfo(string NodeId, double Load, DateTime Timestamp);

/// <summary>
/// JSON 序列化上下文（AOT 兼容）
/// 用于分布式节点发现和消息传递
/// </summary>
[JsonSerializable(typeof(NodeInfo))]
[JsonSerializable(typeof(NodeChangeEvent))]
[JsonSerializable(typeof(HeartbeatInfo))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Default)]
public partial class DistributedJsonContext : JsonSerializerContext
{
}

