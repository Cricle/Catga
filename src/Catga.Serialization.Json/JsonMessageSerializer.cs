using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

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

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Non-generic serialize is marked as non-AOT via DynamicallyAccessedMembers. Users should use generic version or provide JsonSerializerContext for AOT.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Non-generic serialize is marked as non-AOT via DynamicallyAccessedMembers. Users should use generic version or provide JsonSerializerContext for AOT.")]
    public byte[] Serialize(object? value, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        var bufferWriter = new ArrayBufferWriter<byte>(256);
        using var writer = new Utf8JsonWriter(bufferWriter);
        JsonSerializer.Serialize(writer, value, type, _options);
        return bufferWriter.WrittenSpan.ToArray();
    }

    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(byte[] data)
        => Deserialize<T>(data.AsSpan());

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Non-generic deserialize is marked as non-AOT via DynamicallyAccessedMembers. Users should use generic version or provide JsonSerializerContext for AOT.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Non-generic deserialize is marked as non-AOT via DynamicallyAccessedMembers. Users should use generic version or provide JsonSerializerContext for AOT.")]
    public object? Deserialize(byte[] data, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        var reader = new Utf8JsonReader(data);
        return JsonSerializer.Deserialize(ref reader, type, _options);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "JSON serializer is marked as non-AOT via DynamicallyAccessedMembers. Users should provide JsonSerializerContext for AOT.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "JSON serializer is marked as non-AOT via DynamicallyAccessedMembers. Users should provide JsonSerializerContext for AOT.")]
    public void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T value, IBufferWriter<byte> bufferWriter)
    {
        using var writer = new Utf8JsonWriter(bufferWriter);
        JsonSerializer.Serialize(writer, value, _options);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "JSON serializer is marked as non-AOT via DynamicallyAccessedMembers. Users should provide JsonSerializerContext for AOT.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "JSON serializer is marked as non-AOT via DynamicallyAccessedMembers. Users should provide JsonSerializerContext for AOT.")]
    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(ReadOnlySpan<byte> data)
    {
        var reader = new Utf8JsonReader(data);
        return JsonSerializer.Deserialize<T>(ref reader, _options);
    }

    public int GetSizeEstimate<T>(T value) => 256;
}

