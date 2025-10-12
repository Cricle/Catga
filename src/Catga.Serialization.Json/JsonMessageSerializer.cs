using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Catga.Serialization;

namespace Catga.Serialization.Json;

/// <summary>JSON serializer (System.Text.Json, zero-copy, high-performance)</summary>
/// <remarks>
/// <para>For Native AOT compatibility, provide JsonSerializerOptions with a JsonSerializerContext:</para>
/// <code>
/// [JsonSerializable(typeof(MyMessage))]
/// public partial class MyJsonContext : JsonSerializerContext { }
///
/// var options = new JsonSerializerOptions { TypeInfoResolver = MyJsonContext.Default };
/// services.AddCatga().UseJsonSerializer(new JsonMessageSerializer(options));
/// </code>
/// <para>📖 See docs/aot/serialization-aot-guide.md for complete AOT setup guide.</para>
/// </remarks>
public class JsonMessageSerializer : IBufferedMessageSerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>Create JSON serializer with default options (uses reflection, not AOT-compatible)</summary>
    public JsonMessageSerializer() : this(new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = false }) { }

    /// <summary>Create JSON serializer with custom options (for AOT, provide options with JsonSerializerContext)</summary>
    public JsonMessageSerializer(JsonSerializerOptions options) => _options = options;

    public string Name => "JSON";

    public byte[] Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value)
    {
        var bufferWriter = new ArrayBufferWriter<byte>(256);
        Serialize(value, bufferWriter);
        return bufferWriter.WrittenSpan.ToArray();
    }

    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(byte[] data)
        => Deserialize<T>(data.AsSpan());

    public void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value, IBufferWriter<byte> bufferWriter)
    {
        using var writer = new Utf8JsonWriter(bufferWriter);
        JsonSerializer.Serialize(writer, value, _options);
    }

    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(ReadOnlySpan<byte> data)
    {
        var reader = new Utf8JsonReader(data);
        return JsonSerializer.Deserialize<T>(ref reader, _options);
    }

    public int GetSizeEstimate<T>(T value) => 256;
}

