using System.Text.Json;
using System.Text.Json.Serialization;
using Catga.Results;
using Catga.CatGa.Models;

namespace Catga.Serialization;

/// <summary>
/// JSON 源生成器上下文 - 100% AOT 兼容
/// 为所有 Catga 消息类型提供编译时源生成支持
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Default)]
// 基础类型
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(byte[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(List<string>))]
// Catga 核心类型
[JsonSerializable(typeof(CatgaResult<string>))]
[JsonSerializable(typeof(CatgaResult<int>))]
[JsonSerializable(typeof(CatgaResult<bool>))]
[JsonSerializable(typeof(ResultMetadata))]
// CatGa 分布式事务类型
[JsonSerializable(typeof(CatGaContext))]
[JsonSerializable(typeof(CatGaTransactionState))]
[JsonSerializable(typeof(CatGaResult))]
[JsonSerializable(typeof(CatGaOptions))]
public partial class CatgaJsonSerializerContext : JsonSerializerContext
{
}

/// <summary>
/// 扩展方法：为 JsonSerializerOptions 配置 Catga 源生成上下文
/// </summary>
public static class CatgaJsonSerializerContextExtensions
{
    /// <summary>
    /// 添加 Catga JSON 源生成器上下文（AOT 兼容）
    /// </summary>
    public static JsonSerializerOptions UseCatgaContext(this JsonSerializerOptions options)
    {
        options.TypeInfoResolver = CatgaJsonSerializerContext.Default;
        return options;
    }
    
    /// <summary>
    /// 创建默认的 AOT 兼容 JSON 选项
    /// </summary>
    public static JsonSerializerOptions CreateCatgaOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = CatgaJsonSerializerContext.Default
        };
        return options;
    }
}


