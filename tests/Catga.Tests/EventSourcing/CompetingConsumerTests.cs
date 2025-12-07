using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.Stores;
using Catga.Persistence.InMemory.Stores;
using Catga.Resilience;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Unit tests for CompetingConsumer.
/// </summary>
public class CompetingConsumerTests
{
    private readonly InMemoryEventStore _eventStore;
    private readonly InMemorySubscriptionStore _subscriptionStore;

    public CompetingConsumerTests()
    {
        _eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        _subscriptionStore = new InMemorySubscriptionStore();
    }

    [Fact]
    public async Task TryProcessNextAsync_WithEvents_ProcessesOne()
    {
        // Arrange
        await _eventStore.AppendAsync("stream-1", [new TestEvent("a"), new TestEvent("b")]);
        await _subscriptionStore.SaveAsync(new PersistentSubscription("test-sub", "*"));

        var handler = new CountingHandler();
        var consumer = new CompetingConsumer(
            _eventStore, _subscriptionStore, handler, "test-sub", "consumer-1");

        // Act
        var result = await consumer.TryProcessNextAsync();

        // Assert
        result.Should().BeTrue();
        handler.ProcessedCount.Should().Be(1);
    }

    [Fact]
    public async Task TryProcessNextAsync_NoEvents_ReturnsFalse()
    {
        // Arrange
        await _subscriptionStore.SaveAsync(new PersistentSubscription("test-sub", "*"));

        var handler = new CountingHandler();
        var consumer = new CompetingConsumer(
            _eventStore, _subscriptionStore, handler, "test-sub", "consumer-1");

        // Act
        var result = await consumer.TryProcessNextAsync();

        // Assert
        result.Should().BeFalse();
        handler.ProcessedCount.Should().Be(0);
    }

    [Fact]
    public async Task TryProcessNextAsync_NoSubscription_ReturnsFalse()
    {
        // Arrange
        await _eventStore.AppendAsync("stream-1", [new TestEvent("a")]);

        var handler = new CountingHandler();
        var consumer = new CompetingConsumer(
            _eventStore, _subscriptionStore, handler, "nonexistent", "consumer-1");

        // Act
        var result = await consumer.TryProcessNextAsync();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task TryProcessNextAsync_UpdatesSubscriptionPosition()
    {
        // Arrange
        await _eventStore.AppendAsync("stream-1", [new TestEvent("a")]);
        await _subscriptionStore.SaveAsync(new PersistentSubscription("test-sub", "*"));

        var handler = new CountingHandler();
        var consumer = new CompetingConsumer(
            _eventStore, _subscriptionStore, handler, "test-sub", "consumer-1");

        // Act
        await consumer.TryProcessNextAsync();

        // Assert
        var sub = await _subscriptionStore.LoadAsync("test-sub");
        sub!.Position.Should().BeGreaterThan(-1);
        sub.ProcessedCount.Should().Be(1);
    }

    [Fact]
    public async Task TryProcessNextAsync_ReleasesLockAfterProcessing()
    {
        // Arrange
        await _eventStore.AppendAsync("stream-1", [new TestEvent("a")]);
        await _subscriptionStore.SaveAsync(new PersistentSubscription("test-sub", "*"));

        var handler = new CountingHandler();
        var consumer1 = new CompetingConsumer(
            _eventStore, _subscriptionStore, handler, "test-sub", "consumer-1");

        // Act
        await consumer1.TryProcessNextAsync();

        // Assert - another consumer should be able to acquire lock
        var consumer2 = new CompetingConsumer(
            _eventStore, _subscriptionStore, handler, "test-sub", "consumer-2");
        var canAcquire = await _subscriptionStore.TryAcquireLockAsync("test-sub", "consumer-2");
        canAcquire.Should().BeTrue();
    }

    [Fact]
    public async Task TryProcessNextAsync_MatchesStreamPattern()
    {
        // Arrange
        await _eventStore.AppendAsync("orders-1", [new TestEvent("order")]);
        await _eventStore.AppendAsync("customers-1", [new TestEvent("customer")]);
        await _subscriptionStore.SaveAsync(new PersistentSubscription("order-sub", "orders*"));

        var handler = new AccumulatingHandler();
        var consumer = new CompetingConsumer(
            _eventStore, _subscriptionStore, handler, "order-sub", "consumer-1");

        // Act
        await consumer.TryProcessNextAsync();

        // Assert - should only process order events
        handler.Events.Should().Contain("order");
        handler.Events.Should().NotContain("customer");
    }

    #region Test helpers

    private record TestEvent(string Data) : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
    }

    private class CountingHandler : IEventHandler
    {
        public int ProcessedCount { get; private set; }

        public ValueTask HandleAsync(IEvent @event, CancellationToken ct = default)
        {
            ProcessedCount++;
            return ValueTask.CompletedTask;
        }
    }

    private class AccumulatingHandler : IEventHandler
    {
        public List<string> Events { get; } = [];

        public ValueTask HandleAsync(IEvent @event, CancellationToken ct = default)
        {
            if (@event is TestEvent te)
                Events.Add(te.Data);
            return ValueTask.CompletedTask;
        }
    }

    #endregion
}
