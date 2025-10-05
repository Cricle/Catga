using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Catga.Results;

namespace Catga.Nats.Serialization;

/// <summary>
/// NATS JSON 序列化器 - AOT 兼容
/// 支持用户自定义消息类型的序列化
/// </summary>
public static class NatsJsonSerializer
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
    /// 获取 JSON 选项（优先使用自定义选项）
    /// </summary>
    [RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed. Use SetCustomOptions with a JsonSerializerContext for AOT compatibility.")]
    [RequiresDynamicCode("JSON serialization may require dynamic code generation. Use SetCustomOptions with a JsonSerializerContext for AOT compatibility.")]
    public static JsonSerializerOptions GetOptions()
    {
        if (_customOptions != null)
            return _customOptions;

        // 默认选项 - 使用源生成上下文处理已知类型
        // 对于未知类型，fallback 到 reflection（不推荐用于 NativeAOT）
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                NatsCatgaJsonContext.Default,
                // Reflection-based fallback for unknown types
                // This will cause AOT warnings - users should use SetCustomOptions
                new DefaultJsonTypeInfoResolver()
            )
        };
    }

    /// <summary>
    /// 序列化为 UTF-8 字节
    /// 对于 NativeAOT，请使用 SetCustomOptions 提供包含所有消息类型的 JsonSerializerContext
    /// </summary>
    [RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed. Use SetCustomOptions with a JsonSerializerContext for AOT compatibility.")]
    [RequiresDynamicCode("JSON serialization may require dynamic code generation. Use SetCustomOptions with a JsonSerializerContext for AOT compatibility.")]
    public static byte[] SerializeToUtf8Bytes<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, GetOptions());
    }

    /// <summary>
    /// 从 UTF-8 字节反序列化
    /// 对于 NativeAOT，请使用 SetCustomOptions 提供包含所有消息类型的 JsonSerializerContext
    /// </summary>
    [RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed. Use SetCustomOptions with a JsonSerializerContext for AOT compatibility.")]
    [RequiresDynamicCode("JSON serialization may require dynamic code generation. Use SetCustomOptions with a JsonSerializerContext for AOT compatibility.")]
    public static T? Deserialize<T>(ReadOnlySpan<byte> utf8Json)
    {
        return JsonSerializer.Deserialize<T>(utf8Json, GetOptions());
    }

    /// <summary>
    /// 从字符串反序列化
    /// 对于 NativeAOT，请使用 SetCustomOptions 提供包含所有消息类型的 JsonSerializerContext
    /// </summary>
    [RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed. Use SetCustomOptions with a JsonSerializerContext for AOT compatibility.")]
    [RequiresDynamicCode("JSON serialization may require dynamic code generation. Use SetCustomOptions with a JsonSerializerContext for AOT compatibility.")]
    public static T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, GetOptions());
    }

    /// <summary>
    /// 序列化为字符串
    /// 对于 NativeAOT，请使用 SetCustomOptions 提供包含所有消息类型的 JsonSerializerContext
    /// </summary>
    [RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed. Use SetCustomOptions with a JsonSerializerContext for AOT compatibility.")]
    [RequiresDynamicCode("JSON serialization may require dynamic code generation. Use SetCustomOptions with a JsonSerializerContext for AOT compatibility.")]
    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, GetOptions());
    }
}

/// <summary>
/// NATS Catga JSON 源生成器上下文 - 100% AOT 兼容
/// 包含框架内部使用的所有已知类型
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Default)]
// CatgaResult 类型（框架内部）
[JsonSerializable(typeof(CatgaResult))]
// 基础类型
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(byte[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(decimal))]
public partial class NatsCatgaJsonContext : JsonSerializerContext
{
}

