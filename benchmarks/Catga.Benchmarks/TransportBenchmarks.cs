using BenchmarkDotNet.Attributes;
using Catga.Abstractions;
using Catga.Messages;
using Catga.Serialization.Json;
using Catga.Serialization.MemoryPack;
using Catga.Transport;

namespace Catga.Benchmarks;

/// <summary>
/// 消息传输性能测试
/// 测试不同传输层 (InMemory) 的吞吐量和延迟
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class TransportBenchmarks
{
    private InMemoryMessageTransport? _inMemoryTransport;
    private IMessageSerializer? _jsonSerializer;
    private IMessageSerializer? _memoryPackSerializer;
    private TestEvent? _testEvent;
    private List<TestEvent>? _batchEvents;
    private TestRequest? _testRequest;

    [GlobalSetup]
    public void Setup()
    {
        _jsonSerializer = new JsonMessageSerializer();
        _memoryPackSerializer = new MemoryPackMessageSerializer();
        _inMemoryTransport = new InMemoryMessageTransport();

        _testEvent = new TestEvent
        {
            MessageId = Guid.NewGuid().ToString(),
            Id = "test-1",
            Data = "Benchmark test data",
            Timestamp = DateTime.UtcNow
        };

        _batchEvents = Enumerable.Range(0, 1000)
            .Select(i => new TestEvent
            {
                MessageId = Guid.NewGuid().ToString(),
                Id = $"batch-{i}",
                Data = $"Batch data {i}",
                Timestamp = DateTime.UtcNow
            })
            .ToList();

        _testRequest = new TestRequest
        {
            MessageId = Guid.NewGuid().ToString(),
            RequestId = "req-1",
            Data = "Request data"
        };

        // Setup subscribers
        _inMemoryTransport.SubscribeAsync<TestEvent>((msg, ctx) => Task.CompletedTask).Wait();
        _inMemoryTransport.SubscribeAsync<TestRequest>((msg, ctx) => Task.CompletedTask).Wait();
    }

    [Benchmark(Description = "InMemory: Publish Single Event")]
    public async Task InMemory_PublishSingle()
    {
        await _inMemoryTransport!.PublishAsync(_testEvent!);
    }

    [Benchmark(Description = "InMemory: Publish Batch 100")]
    public async Task InMemory_PublishBatch100()
    {
        await _inMemoryTransport!.PublishBatchAsync(_batchEvents!.Take(100));
    }

    [Benchmark(Description = "InMemory: Publish Batch 1000")]
    public async Task InMemory_PublishBatch1000()
    {
        await _inMemoryTransport!.PublishBatchAsync(_batchEvents!);
    }

    [Benchmark(Description = "InMemory: Send Request")]
    public async Task InMemory_SendRequest()
    {
        await _inMemoryTransport!.SendAsync(_testRequest!, "test-destination");
    }

    #region Test Models

    public record TestEvent : IEvent
    {
        public required string MessageId { get; init; }
        public required string Id { get; init; }
        public required string Data { get; init; }
        public DateTime Timestamp { get; init; }
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }

    public record TestRequest : IRequest
    {
        public required string MessageId { get; init; }
        public required string RequestId { get; init; }
        public required string Data { get; init; }
    }

    #endregion
}

