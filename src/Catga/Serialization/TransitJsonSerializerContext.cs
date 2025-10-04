using System.Text.Json;
using System.Text.Json.Serialization;
using Catga.Results;

namespace Catga.Serialization;

/// <summary>
/// JSON 源生成器上下文 - 100% AOT 兼容
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(ResultMetadata))]
public partial class TransitJsonSerializerContext : JsonSerializerContext
{
}

/// <summary>
/// 扩展方法：为 JsonSerializerOptions 配置 Transit 上下文
/// </summary>
public static class TransitJsonSerializerContextExtensions
{
    /// <summary>
    /// 添加 Transit JSON 源生成器上下文（AOT 兼容）
    /// </summary>
    public static JsonSerializerOptions UseTransitContext(this JsonSerializerOptions options)
    {
        options.TypeInfoResolver = TransitJsonSerializerContext.Default;
        return options;
    }
}

