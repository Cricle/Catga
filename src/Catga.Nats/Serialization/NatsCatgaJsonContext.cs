using System.Text.Json;
using System.Text.Json.Serialization;
using Catga.CatGa.Models;
using Catga.Serialization;

namespace Catga.Nats.Serialization;

/// <summary>
/// NATS Catga JSON 源生成器上下文 - 100% AOT 兼容
/// 为 NATS 消息传输提供编译时源生成支持
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Default)]
// NATS 消息封装类型 - 使用具体类型示例
[JsonSerializable(typeof(CatGaMessageWrapper))]
[JsonSerializable(typeof(CatGaResponseWrapper))]
// CatGa 核心类型
[JsonSerializable(typeof(CatGaContext))]
[JsonSerializable(typeof(CatGaTransactionState))]
// 基础类型
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(byte[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(DateTime))]
public partial class NatsCatgaJsonContext : JsonSerializerContext
{
}

/// <summary>
/// CatGa 消息包装器（用于 JSON 序列化）
/// </summary>
public class CatGaMessageWrapper
{
    public object? Request { get; set; }
    public CatGaContext? Context { get; set; }
    public string ServiceId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// CatGa 响应包装器（用于 JSON 序列化）
/// </summary>
public class CatGaResponseWrapper
{
    public bool IsSuccess { get; set; }
    public object? Result { get; set; }
    public string? Error { get; set; }
    public bool IsCompensated { get; set; }
    public string ServiceId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// NATS Catga JSON 扩展方法
/// </summary>
public static class NatsCatgaJsonExtensions
{
    /// <summary>
    /// 创建 AOT 兼容的 JSON 选项（用于 NATS）
    /// </summary>
    public static JsonSerializerOptions CreateNatsCatgaOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // 使用 NATS Catga 源生成上下文
            TypeInfoResolver = NatsCatgaJsonContext.Default
        };
        return options;
    }
}

