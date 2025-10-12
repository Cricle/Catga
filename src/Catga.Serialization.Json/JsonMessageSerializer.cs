using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Catga.Serialization;

namespace Catga.Serialization.Json;

/// <summary>JSON serializer (System.Text.Json, AOT-friendly, zero-copy)</summary>
public class JsonMessageSerializer : IBufferedMessageSerializer
{
    private static readonly JsonSerializerOptions _options = new() { PropertyNameCaseInsensitive = true, WriteIndented = false };

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

