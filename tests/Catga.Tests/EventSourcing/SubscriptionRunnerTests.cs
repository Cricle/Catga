using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.Stores;
using Catga.Persistence.InMemory.Stores;
using Catga.Resilience;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Unit tests for SubscriptionRunner.
/// </summary>
public class SubscriptionRunnerTests
{
    private readonly InMemoryEventStore _eventStore;
    private readonly InMemorySubscriptionStore _subscriptionStore;
    private readonly TestEventHandler _handler;

    public SubscriptionRunnerTests()
    {
        _eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        _subscriptionStore = new InMemorySubscriptionStore();
        _handler = new TestEventHandler();
    }

    [Fact]
    public async Task RunOnceAsync_ProcessesNewEvents()
    {
        // Arrange
        await _eventStore.AppendAsync("Order-1", [new TestOrderEvent { OrderId = "1" }]);
        await _eventStore.AppendAsync("Order-2", [new TestOrderEvent { OrderId = "2" }]);

        var sub = new PersistentSubscription("order-processor", "Order-*");
        await _subscriptionStore.SaveAsync(sub);

        var runner = new SubscriptionRunner(_eventStore, _subscriptionStore, _handler);

        // Act
        await runner.RunOnceAsync("order-processor");

        // Assert
        _handler.ProcessedEvents.Should().HaveCount(2);
    }

    [Fact]
    public async Task RunOnceAsync_UpdatesSubscriptionPosition()
    {
        // Arrange
        await _eventStore.AppendAsync("Order-1", [new TestOrderEvent(), new TestOrderEvent()]);

        var sub = new PersistentSubscription("order-processor", "Order-*");
        await _subscriptionStore.SaveAsync(sub);

        var runner = new SubscriptionRunner(_eventStore, _subscriptionStore, _handler);

        // Act
        await runner.RunOnceAsync("order-processor");

        // Assert
        var updated = await _subscriptionStore.LoadAsync("order-processor");
        updated!.Position.Should().BeGreaterThan(-1);
        updated.ProcessedCount.Should().Be(2);
    }

    [Fact]
    public async Task RunOnceAsync_SkipsAlreadyProcessedEvents()
    {
        // Arrange
        await _eventStore.AppendAsync("Order-1", [new TestOrderEvent(), new TestOrderEvent()]);

        var sub = new PersistentSubscription("order-processor", "Order-*") { Position = 1 };
        await _subscriptionStore.SaveAsync(sub);

        var runner = new SubscriptionRunner(_eventStore, _subscriptionStore, _handler);

        // Act
        await runner.RunOnceAsync("order-processor");

        // Assert - should only process events after position 1
        _handler.ProcessedEvents.Count.Should().BeLessOrEqualTo(1);
    }

    [Fact]
    public async Task RunOnceAsync_FiltersStreamsByPattern()
    {
        // Arrange
        await _eventStore.AppendAsync("Order-1", [new TestOrderEvent { OrderId = "order" }]);
        await _eventStore.AppendAsync("Customer-1", [new TestCustomerEvent { CustomerId = "customer" }]);

        var sub = new PersistentSubscription("order-only", "Order-*");
        await _subscriptionStore.SaveAsync(sub);

        var runner = new SubscriptionRunner(_eventStore, _subscriptionStore, _handler);

        // Act
        await runner.RunOnceAsync("order-only");

        // Assert - should only process Order events
        _handler.ProcessedEvents.Should().AllSatisfy(e => e.Should().BeOfType<TestOrderEvent>());
    }

    [Fact]
    public async Task RunOnceAsync_HandlesNonExistentSubscription()
    {
        // Arrange
        var runner = new SubscriptionRunner(_eventStore, _subscriptionStore, _handler);

        // Act & Assert - should not throw
        await runner.RunOnceAsync("non-existent");
        _handler.ProcessedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_UpdatesLastProcessedAt()
    {
        // Arrange
        await _eventStore.AppendAsync("Order-1", [new TestOrderEvent()]);

        var sub = new PersistentSubscription("order-processor", "Order-*");
        await _subscriptionStore.SaveAsync(sub);

        var runner = new SubscriptionRunner(_eventStore, _subscriptionStore, _handler);

        // Act
        await runner.RunOnceAsync("order-processor");

        // Assert
        var updated = await _subscriptionStore.LoadAsync("order-processor");
        updated!.LastProcessedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    #region Test helpers

    private record TestOrderEvent : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
        public string OrderId { get; init; } = "";
    }

    private record TestCustomerEvent : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
        public string CustomerId { get; init; } = "";
    }

    private class TestEventHandler : IEventHandler
    {
        public List<IEvent> ProcessedEvents { get; } = new();

        public ValueTask HandleAsync(IEvent @event, CancellationToken ct = default)
        {
            ProcessedEvents.Add(@event);
            return ValueTask.CompletedTask;
        }
    }

    #endregion
}
