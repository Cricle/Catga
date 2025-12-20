using BenchmarkDotNet.Attributes;
using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.Stores;
using Catga.Resilience;

namespace Catga.Benchmarks;

/// <summary>
/// Event Sourcing and Time Travel benchmarks.
/// Run: dotnet run -c Release -- --filter *EventSourcing*
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class EventSourcingBenchmarks
{
    private InMemoryEventStore _eventStore = null!;
    private TimeTravelService<TestAggregate> _timeTravelService = null!;

    private string _stream10 = null!;
    private string _stream100 = null!;
    private string _stream1000 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _eventStore = new InMemoryEventStore(new NoopResilienceProvider());
        _timeTravelService = new TimeTravelService<TestAggregate>(_eventStore);

        _stream10 = "TestAggregate-es-10";
        _stream100 = "TestAggregate-es-100";
        _stream1000 = "TestAggregate-es-1000";

        SetupStream(_stream10, 10).GetAwaiter().GetResult();
        SetupStream(_stream100, 100).GetAwaiter().GetResult();
        SetupStream(_stream1000, 1000).GetAwaiter().GetResult();
    }

    private async Task SetupStream(string streamId, int count)
    {
        var id = streamId.Replace("TestAggregate-", "");
        var events = new List<IEvent> { new TestCreatedEvent(id) };
        for (int i = 1; i < count; i++)
            events.Add(new TestUpdatedEvent(id, i));
        await _eventStore.AppendAsync(streamId, events.ToArray());
    }

    #region Append Benchmarks

    [Benchmark(Baseline = true, Description = "Append 1 event")]
    public async Task Append_1()
    {
        var streamId = $"TestAggregate-append-{Guid.NewGuid():N}";
        await _eventStore.AppendAsync(streamId, [new TestCreatedEvent("new")]);
    }

    [Benchmark(Description = "Append 10 events")]
    public async Task Append_10()
    {
        var streamId = $"TestAggregate-append-{Guid.NewGuid():N}";
        var events = new IEvent[10];
        events[0] = new TestCreatedEvent("new");
        for (int i = 1; i < 10; i++)
            events[i] = new TestUpdatedEvent("new", i);
        await _eventStore.AppendAsync(streamId, events);
    }

    [Benchmark(Description = "Append 100 events")]
    public async Task Append_100()
    {
        var streamId = $"TestAggregate-append-{Guid.NewGuid():N}";
        var events = new IEvent[100];
        events[0] = new TestCreatedEvent("new");
        for (int i = 1; i < 100; i++)
            events[i] = new TestUpdatedEvent("new", i);
        await _eventStore.AppendAsync(streamId, events);
    }

    #endregion

    #region Read Benchmarks

    [Benchmark(Description = "Read 10 events")]
    public ValueTask<EventStream> Read_10()
        => _eventStore.ReadAsync(_stream10);

    [Benchmark(Description = "Read 100 events")]
    public ValueTask<EventStream> Read_100()
        => _eventStore.ReadAsync(_stream100);

    [Benchmark(Description = "Read 1000 events")]
    public ValueTask<EventStream> Read_1000()
        => _eventStore.ReadAsync(_stream1000);

    #endregion

    #region Time Travel Benchmarks

    [Benchmark(Description = "ReadToVersion (100 events, v50)")]
    public ValueTask<EventStream> ReadToVersion_100()
        => _eventStore.ReadToVersionAsync(_stream100, 50);

    [Benchmark(Description = "ReadToVersion (1000 events, v500)")]
    public ValueTask<EventStream> ReadToVersion_1000()
        => _eventStore.ReadToVersionAsync(_stream1000, 500);

    [Benchmark(Description = "ReadToTimestamp (100 events)")]
    public ValueTask<EventStream> ReadToTimestamp_100()
        => _eventStore.ReadToTimestampAsync(_stream100, DateTime.UtcNow);

    [Benchmark(Description = "GetVersionHistory (100 events)")]
    public ValueTask<IReadOnlyList<VersionInfo>> GetVersionHistory_100()
        => _eventStore.GetVersionHistoryAsync(_stream100);

    [Benchmark(Description = "GetStateAtVersion (100 events, v50)")]
    public ValueTask<TestAggregate?> GetStateAtVersion_100()
        => _timeTravelService.GetStateAtVersionAsync("es-100", 50);

    [Benchmark(Description = "CompareVersions (100 events, v10-v50)")]
    public ValueTask<StateComparison<TestAggregate>> CompareVersions_100()
        => _timeTravelService.CompareVersionsAsync("es-100", 10, 50);

    #endregion
}

#region Test Domain

public class TestAggregate : AggregateRoot
{
    public override string Id { get; protected set; } = string.Empty;
    public int Value { get; private set; }

    protected override void When(IEvent @event)
    {
        switch (@event)
        {
            case TestCreatedEvent e:
                Id = e.Id;
                break;
            case TestUpdatedEvent e:
                Value = e.Value;
                break;
        }
    }
}

public record TestCreatedEvent(string Id) : IEvent
{
    public long MessageId { get; } = Random.Shared.NextInt64();
    public long? CorrelationId { get; init; }
    public long? CausationId { get; init; }
}

public record TestUpdatedEvent(string Id, int Value) : IEvent
{
    public long MessageId { get; } = Random.Shared.NextInt64();
    public long? CorrelationId { get; init; }
    public long? CausationId { get; init; }
}

#endregion
