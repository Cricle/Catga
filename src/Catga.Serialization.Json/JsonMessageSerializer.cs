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

    public byte[] Serialize<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, _options);
        return Encoding.UTF8.GetBytes(json);
    }

    public T? Deserialize<T>(byte[] data)
    {
        var json = Encoding.UTF8.GetString(data);
        return JsonSerializer.Deserialize<T>(json, _options);
    }
}

