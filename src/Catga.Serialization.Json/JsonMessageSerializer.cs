using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Catga.Serialization;

namespace Catga.Serialization.Json;

/// <summary>
/// JSON 消息序列化器（System.Text.Json）- AOT 友好
/// </summary>
public class JsonMessageSerializer : IMessageSerializer
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public string Name => "JSON";

    [RequiresUnreferencedCode("序列化可能需要无法静态分析的类型")]
    [RequiresDynamicCode("序列化可能需要运行时代码生成")]
    public byte[] Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] T>(T value)
    {
        var json = JsonSerializer.Serialize(value, _options);
        return Encoding.UTF8.GetBytes(json);
    }

    [RequiresUnreferencedCode("反序列化可能需要无法静态分析的类型")]
    [RequiresDynamicCode("反序列化可能需要运行时代码生成")]
    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicConstructors)] T>(byte[] data)
    {
        var json = Encoding.UTF8.GetString(data);
        return JsonSerializer.Deserialize<T>(json, _options);
    }
}

