using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Catga;
using Catga.Serialization.Json;
using Catga.Serialization.MemoryPack;
using MemoryPack;

namespace Catga.Benchmarks;

/// <summary>
/// Serialization performance benchmarks
/// Compares: JSON vs MemoryPack, Pooled vs Non-pooled
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class SerializationBenchmarks
{
    private JsonMessageSerializer _jsonSerializer = null!;
    private MemoryPackMessageSerializer _memoryPackSerializer = null!;
    private TestMessage _message = null!;
    private byte[] _jsonData = null!;
    private byte[] _memoryPackData = null!;

    [GlobalSetup]
    public void Setup()
    {
        _jsonSerializer = new JsonMessageSerializer();
        _memoryPackSerializer = new MemoryPackMessageSerializer();

        _message = new TestMessage
        {
            Id = "test-123",
            Name = "Performance Test",
            Value = 42,
            Timestamp = DateTime.UtcNow,
            Data = new byte[1024] // 1KB payload
        };

        // Pre-serialize for deserialization benchmarks
        _jsonData = _jsonSerializer.Serialize(_message);
        _memoryPackData = _memoryPackSerializer.Serialize(_message);
    }

    #region JSON Serialization

    [Benchmark(Description = "JSON Serialize (pooled)")]
    public byte[] JsonSerialize_Pooled()
    {
        return _jsonSerializer.Serialize(_message);
    }

    [Benchmark(Description = "JSON Deserialize (Span)")]
    public TestMessage? JsonDeserialize_Span()
    {
        return _jsonSerializer.Deserialize<TestMessage>(_jsonData.AsSpan());
    }

    [Benchmark(Description = "JSON Serialize (buffered)")]
    public byte[] JsonSerialize_Buffered()
    {
        using var bufferWriter = new Catga.Core.PooledBufferWriter<byte>(256);
        _jsonSerializer.Serialize(_message, bufferWriter);
        return bufferWriter.WrittenSpan.ToArray();
    }

    #endregion

    #region MemoryPack Serialization

    [Benchmark(Baseline = true, Description = "MemoryPack Serialize")]
    public byte[] MemoryPackSerialize()
    {
        return _memoryPackSerializer.Serialize(_message);
    }

    [Benchmark(Description = "MemoryPack Deserialize (Span)")]
    public TestMessage? MemoryPackDeserialize_Span()
    {
        return _memoryPackSerializer.Deserialize<TestMessage>(_memoryPackData.AsSpan());
    }

    [Benchmark(Description = "MemoryPack Serialize (buffered)")]
    public byte[] MemoryPackSerialize_Buffered()
    {
        using var bufferWriter = new Catga.Core.PooledBufferWriter<byte>(128);
        _memoryPackSerializer.Serialize(_message, bufferWriter);
        return bufferWriter.WrittenSpan.ToArray();
    }

    #endregion

    #region Round-trip benchmarks

    [Benchmark(Description = "JSON Round-trip")]
    public TestMessage? JsonRoundTrip()
    {
        var data = _jsonSerializer.Serialize(_message);
        return _jsonSerializer.Deserialize<TestMessage>(data);
    }

    [Benchmark(Description = "MemoryPack Round-trip")]
    public TestMessage? MemoryPackRoundTrip()
    {
        var data = _memoryPackSerializer.Serialize(_message);
        return _memoryPackSerializer.Deserialize<TestMessage>(data);
    }

    #endregion

}

// Test message type (must be top-level partial for MemoryPack)
[MemoryPackable]
public partial class TestMessage
{
    [MemoryPackOrder(0)]
    public string Id { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public string Name { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public int Value { get; set; }

    [MemoryPackOrder(3)]
    public DateTime Timestamp { get; set; }

    [MemoryPackOrder(4)]
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

