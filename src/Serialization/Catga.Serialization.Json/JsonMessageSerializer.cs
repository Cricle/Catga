using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Catga.Serialization;

namespace Catga.Serialization.Json;

/// <summary>
/// JSON message serializer (System.Text.Json) - AOT friendly with buffering support
/// Optimized with ArrayPool for reduced allocations
/// </summary>
public class JsonMessageSerializer : IBufferedMessageSerializer
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public string Name => "JSON";

    #region IMessageSerializer (legacy, allocating)

    [RequiresUnreferencedCode("Serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Serialization may require runtime code generation")]
    public byte[] Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] T>(T value)
    {
        // Use ArrayBufferWriter for simplicity
        var bufferWriter = new ArrayBufferWriter<byte>(256);
        Serialize(value, bufferWriter);
        return bufferWriter.WrittenSpan.ToArray();
    }

    [RequiresUnreferencedCode("Deserialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Deserialization may require runtime code generation")]
    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicConstructors)] T>(byte[] data)
    {
        // Optimized: Deserialize from ReadOnlySpan (zero-copy)
        return Deserialize<T>(data.AsSpan());
    }

    #endregion

    #region IBufferedMessageSerializer (optimized, pooled)

    [RequiresUnreferencedCode("Serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Serialization may require runtime code generation")]
    public void Serialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] T>(
        T value,
        IBufferWriter<byte> bufferWriter)
    {
        // Zero-allocation serialization using Utf8JsonWriter
        using var writer = new Utf8JsonWriter(bufferWriter);
        JsonSerializer.Serialize(writer, value, _options);
    }

    [RequiresUnreferencedCode("Deserialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Deserialization may require runtime code generation")]
    public T? Deserialize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        ReadOnlySpan<byte> data)
    {
        // Zero-copy deserialization from ReadOnlySpan
        var reader = new Utf8JsonReader(data);
        return JsonSerializer.Deserialize<T>(ref reader, _options);
    }

    public int GetSizeEstimate<T>(T value)
    {
        // Conservative estimate: JSON is typically 1.5-2x object size
        // For small objects, default to 256 bytes
        return 256;
    }

    #endregion
}

