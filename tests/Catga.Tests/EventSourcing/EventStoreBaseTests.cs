using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Resilience;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Comprehensive tests for EventStoreBase
/// </summary>
public class EventStoreBaseTests
{
    private readonly IResiliencePipelineProvider _resilienceProvider;
    private readonly IEventTypeRegistry _typeRegistry;
    private readonly TestEventStore _eventStore;

    public EventStoreBaseTests()
    {
        _resilienceProvider = Substitute.For<IResiliencePipelineProvider>();
        _typeRegistry = Substitute.For<IEventTypeRegistry>();
        
        // Setup resilience provider to execute immediately
        _resilienceProvider.ExecutePersistenceAsync(
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var func = callInfo.Arg<Func<CancellationToken, ValueTask>>();
                return func(CancellationToken.None);
            });
        
        _resilienceProvider.ExecutePersistenceAsync(
            Arg.Any<Func<CancellationToken, ValueTask<EventStream>>>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var func = callInfo.Arg<Func<CancellationToken, ValueTask<EventStream>>>();
                return func(CancellationToken.None);
            });
        
        _resilienceProvider.ExecutePersistenceAsync(
            Arg.Any<Func<CancellationToken, ValueTask<long>>>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var func = callInfo.Arg<Func<CancellationToken, ValueTask<long>>>();
                return func(CancellationToken.None);
            });
        
        _resilienceProvider.ExecutePersistenceAsync(
            Arg.Any<Func<CancellationToken, ValueTask<IReadOnlyList<VersionInfo>>>>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var func = callInfo.Arg<Func<CancellationToken, ValueTask<IReadOnlyList<VersionInfo>>>>();
                return func(CancellationToken.None);
            });
        
        _resilienceProvider.ExecutePersistenceAsync(
            Arg.Any<Func<CancellationToken, ValueTask<IReadOnlyList<string>>>>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var func = callInfo.Arg<Func<CancellationToken, ValueTask<IReadOnlyList<string>>>>();
                return func(CancellationToken.None);
            });
        
        _eventStore = new TestEventStore(_resilienceProvider, _typeRegistry);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullResilienceProvider_ShouldThrow()
    {
        var act = () => new TestEventStore(null!, _typeRegistry);
        
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("resilienceProvider");
    }

    [Fact]
    public void Constructor_WithNullTypeRegistry_ShouldUseDefault()
    {
        var store = new TestEventStore(_resilienceProvider, null);
        
        store.Should().NotBeNull();
    }

    #endregion

    #region AppendAsync Tests

    [Fact]
    public async Task AppendAsync_WithValidEvents_ShouldCallCore()
    {
        var events = new List<IEvent> { new TestEvent { EventId = 1 } };
        
        await _eventStore.AppendAsync("stream-1", events, 0);
        
        _eventStore.AppendCoreCalled.Should().BeTrue();
        _eventStore.LastStreamId.Should().Be("stream-1");
        _eventStore.LastEvents.Should().BeEquivalentTo(events);
    }

    [Fact]
    public async Task AppendAsync_WithEmptyEvents_ShouldNotCallCore()
    {
        var events = new List<IEvent>();
        
        await _eventStore.AppendAsync("stream-1", events);
        
        _eventStore.AppendCoreCalled.Should().BeFalse();
    }

    [Fact]
    public async Task AppendAsync_WithNullStreamId_ShouldThrow()
    {
        var events = new List<IEvent> { new TestEvent() };
        
        var act = async () => await _eventStore.AppendAsync(null!, events);
        
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AppendAsync_WithEmptyStreamId_ShouldThrow()
    {
        var events = new List<IEvent> { new TestEvent() };
        
        var act = async () => await _eventStore.AppendAsync("", events);
        
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AppendAsync_WithWhitespaceStreamId_ShouldThrow()
    {
        var events = new List<IEvent> { new TestEvent() };
        
        var act = async () => await _eventStore.AppendAsync("   ", events);
        
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AppendAsync_WithNullEvents_ShouldThrow()
    {
        var act = async () => await _eventStore.AppendAsync("stream-1", null!);
        
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task AppendAsync_WhenCoreThrows_ShouldPropagate()
    {
        _eventStore.ThrowOnAppend = true;
        var events = new List<IEvent> { new TestEvent() };
        
        var act = async () => await _eventStore.AppendAsync("stream-1", events);
        
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Append failed");
    }

    #endregion

    #region ReadAsync Tests

    [Fact]
    public async Task ReadAsync_WithValidStreamId_ShouldCallCore()
    {
        var result = await _eventStore.ReadAsync("stream-1");
        
        _eventStore.ReadCoreCalled.Should().BeTrue();
        result.StreamId.Should().Be("stream-1");
    }

    [Fact]
    public async Task ReadAsync_WithNullStreamId_ShouldThrow()
    {
        var act = async () => await _eventStore.ReadAsync(null!);
        
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReadAsync_WithFromVersionAndMaxCount_ShouldPassParameters()
    {
        await _eventStore.ReadAsync("stream-1", fromVersion: 5, maxCount: 10);
        
        _eventStore.LastFromVersion.Should().Be(5);
        _eventStore.LastMaxCount.Should().Be(10);
    }

    [Fact]
    public async Task ReadAsync_WhenCoreThrows_ShouldPropagate()
    {
        _eventStore.ThrowOnRead = true;
        
        var act = async () => await _eventStore.ReadAsync("stream-1");
        
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Read failed");
    }

    #endregion

    #region GetVersionAsync Tests

    [Fact]
    public async Task GetVersionAsync_WithValidStreamId_ShouldReturnVersion()
    {
        _eventStore.VersionToReturn = 42;
        
        var version = await _eventStore.GetVersionAsync("stream-1");
        
        version.Should().Be(42);
    }

    [Fact]
    public async Task GetVersionAsync_WithNullStreamId_ShouldThrow()
    {
        var act = async () => await _eventStore.GetVersionAsync(null!);
        
        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region ReadToVersionAsync Tests

    [Fact]
    public async Task ReadToVersionAsync_WithValidParameters_ShouldCallCore()
    {
        var result = await _eventStore.ReadToVersionAsync("stream-1", 10);
        
        _eventStore.ReadToVersionCoreCalled.Should().BeTrue();
        _eventStore.LastToVersion.Should().Be(10);
    }

    [Fact]
    public async Task ReadToVersionAsync_WithNullStreamId_ShouldThrow()
    {
        var act = async () => await _eventStore.ReadToVersionAsync(null!, 10);
        
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReadToVersionAsync_WhenCoreThrows_ShouldPropagate()
    {
        _eventStore.ThrowOnReadToVersion = true;
        
        var act = async () => await _eventStore.ReadToVersionAsync("stream-1", 10);
        
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("ReadToVersion failed");
    }

    #endregion

    #region ReadToTimestampAsync Tests

    [Fact]
    public async Task ReadToTimestampAsync_WithValidParameters_ShouldCallCore()
    {
        var timestamp = DateTime.UtcNow;
        
        var result = await _eventStore.ReadToTimestampAsync("stream-1", timestamp);
        
        _eventStore.ReadToTimestampCoreCalled.Should().BeTrue();
        _eventStore.LastUpperBound.Should().Be(timestamp);
    }

    [Fact]
    public async Task ReadToTimestampAsync_WithNullStreamId_ShouldThrow()
    {
        var act = async () => await _eventStore.ReadToTimestampAsync(null!, DateTime.UtcNow);
        
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReadToTimestampAsync_WhenCoreThrows_ShouldPropagate()
    {
        _eventStore.ThrowOnReadToTimestamp = true;
        
        var act = async () => await _eventStore.ReadToTimestampAsync("stream-1", DateTime.UtcNow);
        
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("ReadToTimestamp failed");
    }

    #endregion

    #region GetVersionHistoryAsync Tests

    [Fact]
    public async Task GetVersionHistoryAsync_WithValidStreamId_ShouldReturnHistory()
    {
        var result = await _eventStore.GetVersionHistoryAsync("stream-1");
        
        _eventStore.GetVersionHistoryCoreCalled.Should().BeTrue();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetVersionHistoryAsync_WithNullStreamId_ShouldThrow()
    {
        var act = async () => await _eventStore.GetVersionHistoryAsync(null!);
        
        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region GetAllStreamIdsAsync Tests

    [Fact]
    public async Task GetAllStreamIdsAsync_ShouldReturnStreamIds()
    {
        _eventStore.StreamIdsToReturn = ["stream-1", "stream-2"];
        
        var result = await _eventStore.GetAllStreamIdsAsync();
        
        result.Should().BeEquivalentTo(["stream-1", "stream-2"]);
    }

    #endregion

    #region Test Helpers

    public class TestEventStore : EventStoreBase
    {
        public bool AppendCoreCalled { get; private set; }
        public bool ReadCoreCalled { get; private set; }
        public bool ReadToVersionCoreCalled { get; private set; }
        public bool ReadToTimestampCoreCalled { get; private set; }
        public bool GetVersionHistoryCoreCalled { get; private set; }
        
        public string? LastStreamId { get; private set; }
        public IReadOnlyList<IEvent>? LastEvents { get; private set; }
        public long LastFromVersion { get; private set; }
        public int LastMaxCount { get; private set; }
        public long LastToVersion { get; private set; }
        public DateTime LastUpperBound { get; private set; }
        
        public bool ThrowOnAppend { get; set; }
        public bool ThrowOnRead { get; set; }
        public bool ThrowOnReadToVersion { get; set; }
        public bool ThrowOnReadToTimestamp { get; set; }
        
        public long VersionToReturn { get; set; } = 0;
        public IReadOnlyList<string> StreamIdsToReturn { get; set; } = [];

        public TestEventStore(
            IResiliencePipelineProvider resilienceProvider,
            IEventTypeRegistry? typeRegistry = null)
            : base(resilienceProvider, typeRegistry)
        {
        }

        protected override ValueTask AppendCoreAsync(
            string streamId,
            IReadOnlyList<IEvent> events,
            long expectedVersion,
            CancellationToken ct)
        {
            if (ThrowOnAppend)
                throw new InvalidOperationException("Append failed");
            
            AppendCoreCalled = true;
            LastStreamId = streamId;
            LastEvents = events;
            return ValueTask.CompletedTask;
        }

        protected override ValueTask<EventStream> ReadCoreAsync(
            string streamId,
            long fromVersion,
            int maxCount,
            CancellationToken ct)
        {
            if (ThrowOnRead)
                throw new InvalidOperationException("Read failed");
            
            ReadCoreCalled = true;
            LastStreamId = streamId;
            LastFromVersion = fromVersion;
            LastMaxCount = maxCount;
            return ValueTask.FromResult(new EventStream
            {
                StreamId = streamId,
                Version = 0,
                Events = []
            });
        }

        protected override ValueTask<long> GetVersionCoreAsync(string streamId, CancellationToken ct)
        {
            return ValueTask.FromResult(VersionToReturn);
        }

        protected override ValueTask<EventStream> ReadToVersionCoreAsync(
            string streamId,
            long toVersion,
            CancellationToken ct)
        {
            if (ThrowOnReadToVersion)
                throw new InvalidOperationException("ReadToVersion failed");
            
            ReadToVersionCoreCalled = true;
            LastToVersion = toVersion;
            return ValueTask.FromResult(new EventStream
            {
                StreamId = streamId,
                Version = toVersion,
                Events = []
            });
        }

        protected override ValueTask<EventStream> ReadToTimestampCoreAsync(
            string streamId,
            DateTime upperBound,
            CancellationToken ct)
        {
            if (ThrowOnReadToTimestamp)
                throw new InvalidOperationException("ReadToTimestamp failed");
            
            ReadToTimestampCoreCalled = true;
            LastUpperBound = upperBound;
            return ValueTask.FromResult(new EventStream
            {
                StreamId = streamId,
                Version = 0,
                Events = []
            });
        }

        protected override ValueTask<IReadOnlyList<VersionInfo>> GetVersionHistoryCoreAsync(
            string streamId,
            CancellationToken ct)
        {
            GetVersionHistoryCoreCalled = true;
            return ValueTask.FromResult<IReadOnlyList<VersionInfo>>([]);
        }

        protected override ValueTask<IReadOnlyList<string>> GetAllStreamIdsCoreAsync(CancellationToken ct)
        {
            return ValueTask.FromResult(StreamIdsToReturn);
        }
    }

    public record TestEvent : IEvent
    {
        public long EventId { get; init; }
        public long MessageId { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }

    #endregion
}
