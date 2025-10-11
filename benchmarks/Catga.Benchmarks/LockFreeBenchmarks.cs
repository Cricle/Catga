using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Catga.EventSourcing;
using Catga.Messages;

namespace Catga.Benchmarks;

/// <summary>
/// Benchmark for lock-free implementations
/// 测试无锁实现的性能
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class LockFreeBenchmarks
{
    private MemoryEventStore _eventStore = null!;
    private TestEvent[] _singleEvent = null!;
    private TestEvent[] _smallBatch = null!;
    private TestEvent[] _largeBatch = null!;

    [GlobalSetup]
    public void Setup()
    {
        _eventStore = new MemoryEventStore();
        _singleEvent = new[] { new TestEvent { Data = "Test" } };
        _smallBatch = Enumerable.Range(0, 10).Select(i => new TestEvent { Data = $"Event {i}" }).ToArray();
        _largeBatch = Enumerable.Range(0, 100).Select(i => new TestEvent { Data = $"Event {i}" }).ToArray();
    }

    [Benchmark(Description = "Append Single Event (Lock-Free)")]
    public async Task AppendSingleEvent()
    {
        await _eventStore.AppendAsync($"stream-{Guid.NewGuid()}", _singleEvent);
    }

    [Benchmark(Description = "Append Small Batch (10 events, Lock-Free)")]
    public async Task AppendSmallBatch()
    {
        await _eventStore.AppendAsync($"stream-{Guid.NewGuid()}", _smallBatch);
    }

    [Benchmark(Description = "Append Large Batch (100 events, Lock-Free)")]
    public async Task AppendLargeBatch()
    {
        await _eventStore.AppendAsync($"stream-{Guid.NewGuid()}", _largeBatch);
    }

    [Benchmark(Description = "Read Stream (Lock-Free)")]
    public async Task<EventStream> ReadStream()
    {
        var streamId = $"stream-read-{Guid.NewGuid()}";
        await _eventStore.AppendAsync(streamId, _smallBatch);
        return await _eventStore.ReadAsync(streamId);
    }

    [Benchmark(Description = "Get Version (Lock-Free)")]
    public async Task<long> GetVersion()
    {
        var streamId = $"stream-version-{Guid.NewGuid()}";
        await _eventStore.AppendAsync(streamId, _singleEvent);
        return await _eventStore.GetVersionAsync(streamId);
    }

    [Benchmark(Description = "Concurrent Append (8 threads, Lock-Free)")]
    [Arguments(8)]
    public async Task ConcurrentAppend(int threadCount)
    {
        var streamId = $"stream-concurrent-{Guid.NewGuid()}";
        var tasks = new Task[threadCount];

        for (int i = 0; i < threadCount; i++)
        {
            var localI = i;
            tasks[i] = Task.Run(async () =>
            {
                var events = new[] { new TestEvent { Data = $"Thread-{localI}" } };
                await _eventStore.AppendAsync(streamId, events);
            });
        }

        await Task.WhenAll(tasks);
    }

    [Benchmark(Description = "Concurrent Read (16 threads, Lock-Free)")]
    [Arguments(16)]
    public async Task ConcurrentRead(int threadCount)
    {
        var streamId = $"stream-read-concurrent-{Guid.NewGuid()}";
        await _eventStore.AppendAsync(streamId, _largeBatch);

        var tasks = new Task<EventStream>[threadCount];

        for (int i = 0; i < threadCount; i++)
        {
            tasks[i] = Task.Run(async () => await _eventStore.ReadAsync(streamId));
        }

        await Task.WhenAll(tasks);
    }

    [Benchmark(Description = "Mixed Read/Write (Lock-Free)")]
    public async Task MixedReadWrite()
    {
        var streamId = $"stream-mixed-{Guid.NewGuid()}";
        
        var writeTasks = new Task[5];
        var readTasks = new Task<EventStream>[5];

        for (int i = 0; i < 5; i++)
        {
            writeTasks[i] = Task.Run(async () =>
            {
                await _eventStore.AppendAsync(streamId, _singleEvent);
            });

            readTasks[i] = Task.Run(async () =>
            {
                return await _eventStore.ReadAsync(streamId);
            });
        }

        await Task.WhenAll(writeTasks.Concat<Task>(readTasks));
    }
}

/// <summary>
/// Test event for benchmarking
/// </summary>
public class TestEvent : IEvent
{
    public string Data { get; set; } = string.Empty;
}

