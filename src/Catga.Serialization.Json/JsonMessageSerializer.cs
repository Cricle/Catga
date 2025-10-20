using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Catga.Serialization.Json;

/// <summary>
/// JSON serializer (System.Text.Json, AOT-safe)
/// </summary>
/// <remarks>
/// For Native AOT compatibility, provide JsonSerializerOptions with a JsonSerializerContext:
/// <code>
/// [JsonSerializable(typeof(MyMessage))]
/// public partial class MyJsonContext : JsonSerializerContext { }
///
/// var options = new JsonSerializerOptions { TypeInfoResolver = MyJsonContext.Default };
/// services.AddCatga().UseJsonSerializer(new JsonMessageSerializer(options));
/// </code>
/// </remarks>
public class JsonMessageSerializer : MessageSerializerBase
{
    private readonly JsonSerializerOptions _options;

    /// <summary>Create JSON serializer with default options</summary>
    public JsonMessageSerializer()
        : this(new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = false }) { }

    /// <summary>Create JSON serializer with custom options</summary>
    public JsonMessageSerializer(JsonSerializerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public override string Name => "JSON";

    /// <summary>
    /// Serialize to buffer writer
    /// </summary>
    public override void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        T value,
        IBufferWriter<byte> bufferWriter)
    {
        using var writer = new Utf8JsonWriter(bufferWriter);
        JsonSerializer.Serialize(writer, value, _options);
    }

    /// <summary>
    /// Deserialize from span
    /// </summary>
    public override T Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        ReadOnlySpan<byte> data)
    {
        var reader = new Utf8JsonReader(data);
        return JsonSerializer.Deserialize<T>(ref reader, _options)!;
    }

    /// <summary>
    /// Estimate serialized size for buffer allocation
    /// </summary>
    protected override int GetSizeEstimate<T>(T value) => 256;
}
