using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.Stores;
using Catga.Resilience;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Benchmarks;

/// <summary>
/// Time travel performance benchmarks - measures event store time travel operations.
/// Run: dotnet run -c Release --filter *TimeTravel*
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class TimeTravelBenchmarks
{
    private InMemoryEventStore _eventStore = null!;
    private TimeTravelService<BenchAggregate> _timeTravelService = null!;
    private string _streamId10 = null!;
    private string _streamId100 = null!;
    private string _streamId1000 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _eventStore = new InMemoryEventStore(new NoopResiliencePipelineProvider());

        // Setup streams with different event counts
        _streamId10 = "BenchAggregate-bench-10";
        _streamId100 = "BenchAggregate-bench-100";
        _streamId1000 = "BenchAggregate-bench-1000";

        SetupStream(_streamId10, 10).GetAwaiter().GetResult();
        SetupStream(_streamId100, 100).GetAwaiter().GetResult();
        SetupStream(_streamId1000, 1000).GetAwaiter().GetResult();

        _timeTravelService = new TimeTravelService<BenchAggregate>(_eventStore);
    }

    private async Task SetupStream(string streamId, int eventCount)
    {
        var events = new List<IEvent> { new BenchCreatedEvent { Id = streamId.Replace("BenchAggregate-", "") } };
        for (int i = 1; i < eventCount; i++)
        {
            events.Add(new BenchUpdatedEvent { Id = streamId.Replace("BenchAggregate-", ""), Value = i });
        }
        await _eventStore.AppendAsync(streamId, events.ToArray());
    }

    #region ReadToVersion Benchmarks

    [Benchmark(Description = "ReadToVersion (10 events, v5)")]
    public async ValueTask<EventStream> ReadToVersion_10Events()
    {
        return await _eventStore.ReadToVersionAsync(_streamId10, 5);
    }

    [Benchmark(Description = "ReadToVersion (100 events, v50)")]
    public async ValueTask<EventStream> ReadToVersion_100Events()
    {
        return await _eventStore.ReadToVersionAsync(_streamId100, 50);
    }

    [Benchmark(Description = "ReadToVersion (1000 events, v500)")]
    public async ValueTask<EventStream> ReadToVersion_1000Events()
    {
        return await _eventStore.ReadToVersionAsync(_streamId1000, 500);
    }

    #endregion

    #region ReadToTimestamp Benchmarks

    [Benchmark(Description = "ReadToTimestamp (10 events)")]
    public async ValueTask<EventStream> ReadToTimestamp_10Events()
    {
        return await _eventStore.ReadToTimestampAsync(_streamId10, DateTime.UtcNow);
    }

    [Benchmark(Description = "ReadToTimestamp (100 events)")]
    public async ValueTask<EventStream> ReadToTimestamp_100Events()
    {
        return await _eventStore.ReadToTimestampAsync(_streamId100, DateTime.UtcNow);
    }

    [Benchmark(Description = "ReadToTimestamp (1000 events)")]
    public async ValueTask<EventStream> ReadToTimestamp_1000Events()
    {
        return await _eventStore.ReadToTimestampAsync(_streamId1000, DateTime.UtcNow);
    }

    #endregion

    #region GetVersionHistory Benchmarks

    [Benchmark(Description = "GetVersionHistory (10 events)")]
    public async ValueTask<IReadOnlyList<VersionInfo>> GetVersionHistory_10Events()
    {
        return await _eventStore.GetVersionHistoryAsync(_streamId10);
    }

    [Benchmark(Description = "GetVersionHistory (100 events)")]
    public async ValueTask<IReadOnlyList<VersionInfo>> GetVersionHistory_100Events()
    {
        return await _eventStore.GetVersionHistoryAsync(_streamId100);
    }

    [Benchmark(Description = "GetVersionHistory (1000 events)")]
    public async ValueTask<IReadOnlyList<VersionInfo>> GetVersionHistory_1000Events()
    {
        return await _eventStore.GetVersionHistoryAsync(_streamId1000);
    }

    #endregion

    #region TimeTravelService Benchmarks

    [Benchmark(Description = "GetStateAtVersion (10 events)")]
    public async ValueTask<BenchAggregate?> GetStateAtVersion_10Events()
    {
        return await _timeTravelService.GetStateAtVersionAsync("bench-10", 5);
    }

    [Benchmark(Description = "GetStateAtVersion (100 events)")]
    public async ValueTask<BenchAggregate?> GetStateAtVersion_100Events()
    {
        return await _timeTravelService.GetStateAtVersionAsync("bench-100", 50);
    }

    [Benchmark(Description = "GetStateAtVersion (1000 events)")]
    public async ValueTask<BenchAggregate?> GetStateAtVersion_1000Events()
    {
        return await _timeTravelService.GetStateAtVersionAsync("bench-1000", 500);
    }

    [Benchmark(Description = "CompareVersions (100 events, v0-v50)")]
    public async ValueTask<StateComparison<BenchAggregate>> CompareVersions_100Events()
    {
        return await _timeTravelService.CompareVersionsAsync("bench-100", 0, 50);
    }

    #endregion

    #region Test Domain

    public class BenchAggregate : AggregateRoot
    {
        public override string Id { get; protected set; } = string.Empty;
        public int Value { get; private set; }

        protected override void When(IEvent @event)
        {
            switch (@event)
            {
                case BenchCreatedEvent e:
                    Id = e.Id;
                    break;
                case BenchUpdatedEvent e:
                    Value = e.Value;
                    break;
            }
        }
    }

    private record BenchCreatedEvent : IEvent
    {
        public long MessageId { get; init; } = Random.Shared.NextInt64();
        public long? CorrelationId { get; init; }
        public long? CausationId { get; init; }
        public required string Id { get; init; }
    }

    private record BenchUpdatedEvent : IEvent
    {
        public long MessageId { get; init; } = Random.Shared.NextInt64();
        public long? CorrelationId { get; init; }
        public long? CausationId { get; init; }
        public required string Id { get; init; }
        public required int Value { get; init; }
    }

    #endregion
}

