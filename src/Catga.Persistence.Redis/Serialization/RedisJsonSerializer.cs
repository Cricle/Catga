using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Catga.Persistence.Redis.Serialization;

/// <summary>
/// Redis JSON 序列化器 - AOT 兼容
/// 支持 Outbox/Inbox 消息类型的序列化
/// </summary>
public static class RedisJsonSerializer
{
    private static JsonSerializerOptions? _customOptions;

    /// <summary>
    /// 设置自定义 JSON 选项（用于用户定义的消息类型）
    /// 对于完全的 AOT 兼容，用户应该提供包含所有消息类型的 JsonSerializerContext
    /// </summary>
    public static void SetCustomOptions(JsonSerializerOptions options)
    {
        _customOptions = options;
    }

    /// <summary>
    /// Get JSON options (uses custom options if provided, or default AOT-compatible context)
    /// </summary>
    /// <remarks>
    /// For full AOT compatibility, call SetCustomOptions() with a JsonSerializerContext
    /// that includes all your message types. Without custom options, only basic types
    /// defined in RedisCatgaJsonContext can be serialized.
    /// </remarks>
    public static JsonSerializerOptions GetOptions()
    {
        if (_customOptions != null)
            return _customOptions;

        // Default options - ONLY uses source-generated context (100% AOT compatible)
        // No reflection fallback - users MUST call SetCustomOptions for custom types
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = RedisCatgaJsonContext.Default  // AOT-only, no reflection
        };
    }

    /// <summary>
    /// 序列化为字符串
    /// 对于 NativeAOT，请使用 SetCustomOptions 提供包含所有消息类型的 JsonSerializerContext
    /// </summary>
    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, GetOptions());
    }

    /// <summary>
    /// 从字符串反序列化
    /// 对于 NativeAOT，请使用 SetCustomOptions 提供包含所有消息类型的 JsonSerializerContext
    /// </summary>
    public static T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, GetOptions());
    }
}

/// <summary>
/// Redis Catga JSON 源生成器上下文 - 100% AOT 兼容
/// 包含 Redis 相关的所有已知类型
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Default)]
// 基础类型
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(byte[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
public partial class RedisCatgaJsonContext : JsonSerializerContext
{
}

