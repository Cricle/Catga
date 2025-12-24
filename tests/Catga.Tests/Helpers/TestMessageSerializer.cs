using System.Buffers;
using System.Text.Json;
using Catga.Abstractions;

namespace Catga.Tests.Helpers;

/// <summary>
/// Test message serializer using System.Text.Json for testing purposes only.
/// </summary>
public sealed class TestMessageSerializer : IMessageSerializer
{
    public string Name => "test-json";
    
    public byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value);
    
    public T Deserialize<T>(byte[] data) => JsonSerializer.Deserialize<T>(data)!;
    
    public T Deserialize<T>(ReadOnlySpan<byte> data) => JsonSerializer.Deserialize<T>(data)!;
    
    public void Serialize<T>(T value, IBufferWriter<byte> bufferWriter)
    {
        using var writer = new Utf8JsonWriter(bufferWriter);
        JsonSerializer.Serialize(writer, value);
    }
    
    public byte[] Serialize(object value, Type type) => JsonSerializer.SerializeToUtf8Bytes(value, type);
    
    public object? Deserialize(byte[] data, Type type) => JsonSerializer.Deserialize(data, type);
    
    public object? Deserialize(ReadOnlySpan<byte> data, Type type) => JsonSerializer.Deserialize(data, type);
    
    public void Serialize(object value, Type type, IBufferWriter<byte> bufferWriter)
    {
        using var writer = new Utf8JsonWriter(bufferWriter);
        JsonSerializer.Serialize(writer, value, type);
    }
    
    public int GetSizeEstimate<T>(T value) => 1024;
}
