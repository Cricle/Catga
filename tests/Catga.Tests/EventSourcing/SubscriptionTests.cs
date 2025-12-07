using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.Stores;
using Catga.Persistence.InMemory.Stores;
using Catga.Resilience;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// TDD tests for event subscription functionality.
/// Subscriptions allow consumers to receive events in real-time or from a specific position.
/// </summary>
public class SubscriptionTests
{
    private readonly InMemoryEventStore _eventStore;
    private readonly InMemorySubscriptionStore _subscriptionStore;

    public SubscriptionTests()
    {
        _eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        _subscriptionStore = new InMemorySubscriptionStore();
    }

    #region 1. Persistent subscription (survives restarts)

    [Fact]
    public async Task PersistentSubscription_SavesPosition()
    {
        // Arrange
        var subscription = new PersistentSubscription("test-sub", "Order-*");
        await _subscriptionStore.SaveAsync(subscription);

        // Act
        subscription.UpdatePosition(5);
        await _subscriptionStore.SaveAsync(subscription);

        // Assert
        var loaded = await _subscriptionStore.LoadAsync("test-sub");
        loaded.Should().NotBeNull();
        loaded!.Position.Should().Be(5);
    }

    [Fact]
    public async Task PersistentSubscription_ResumesFromLastPosition()
    {
        // Arrange
        var streamId = "Order-order-1";
        var events = new IEvent[]
        {
            new TestOrderEvent { OrderId = "order-1", Amount = 100 },
            new TestOrderEvent { OrderId = "order-1", Amount = 200 },
            new TestOrderEvent { OrderId = "order-1", Amount = 300 }
        };
        await _eventStore.AppendAsync(streamId, events);

        // Save subscription at position 1
        var subscription = new PersistentSubscription("test-sub", "Order-*") { Position = 1 };
        await _subscriptionStore.SaveAsync(subscription);

        // Act
        var handler = new TestEventHandler();
        var runner = new SubscriptionRunner(_eventStore, _subscriptionStore, handler);
        await runner.RunOnceAsync("test-sub");

        // Assert - Only events after position 1 should be processed
        handler.ProcessedEvents.Should().HaveCount(1);
        ((TestOrderEvent)handler.ProcessedEvents[0]).Amount.Should().Be(300);
    }

    #endregion

    #region 2. Competing consumers (load balancing)

    [Fact]
    public async Task CompetingConsumers_OnlyOneProcessesEvent()
    {
        // Arrange
        var streamId = "Order-order-1";
        await _eventStore.AppendAsync(streamId, [new TestOrderEvent { OrderId = "order-1", Amount = 100 }]);

        var subscription = new PersistentSubscription("shared-sub", "Order-*");
        await _subscriptionStore.SaveAsync(subscription);

        var handler1 = new TestEventHandler();
        var handler2 = new TestEventHandler();

        // Act - Both consumers try to process
        var consumer1 = new CompetingConsumer(_eventStore, _subscriptionStore, handler1, "shared-sub", "consumer-1");
        var consumer2 = new CompetingConsumer(_eventStore, _subscriptionStore, handler2, "shared-sub", "consumer-2");

        var task1 = consumer1.TryProcessNextAsync().AsTask();
        var task2 = consumer2.TryProcessNextAsync().AsTask();
        await Task.WhenAll(task1, task2);

        // Assert - Only one should have processed
        var totalProcessed = handler1.ProcessedEvents.Count + handler2.ProcessedEvents.Count;
        totalProcessed.Should().Be(1);
    }

    [Fact]
    public async Task CompetingConsumers_DistributesLoad()
    {
        // Arrange
        var streamId = "Order-order-1";
        var events = Enumerable.Range(1, 10)
            .Select(i => new TestOrderEvent { OrderId = $"order-{i}", Amount = i * 100 })
            .Cast<IEvent>()
            .ToArray();
        await _eventStore.AppendAsync(streamId, events);

        var subscription = new PersistentSubscription("shared-sub", "Order-*");
        await _subscriptionStore.SaveAsync(subscription);

        var handler1 = new TestEventHandler();
        var handler2 = new TestEventHandler();
        var consumer1 = new CompetingConsumer(_eventStore, _subscriptionStore, handler1, "shared-sub", "consumer-1");
        var consumer2 = new CompetingConsumer(_eventStore, _subscriptionStore, handler2, "shared-sub", "consumer-2");

        // Act - Process all events
        for (int i = 0; i < 10; i++)
        {
            if (i % 2 == 0)
                await consumer1.TryProcessNextAsync();
            else
                await consumer2.TryProcessNextAsync();
        }

        // Assert - Both should have processed some events
        var total = handler1.ProcessedEvents.Count + handler2.ProcessedEvents.Count;
        total.Should().Be(10);
    }

    #endregion

    #region 3. Event filtering (by type or stream pattern)

    [Fact]
    public async Task Subscription_FiltersByStreamPattern()
    {
        // Arrange
        await _eventStore.AppendAsync("Order-order-1", [new TestOrderEvent { OrderId = "order-1", Amount = 100 }]);
        await _eventStore.AppendAsync("Customer-cust-1", [new TestCustomerEvent { CustomerId = "cust-1" }]);
        await _eventStore.AppendAsync("Order-order-2", [new TestOrderEvent { OrderId = "order-2", Amount = 200 }]);

        var subscription = new PersistentSubscription("order-sub", "Order-*");
        await _subscriptionStore.SaveAsync(subscription);

        var handler = new TestEventHandler();
        var runner = new SubscriptionRunner(_eventStore, _subscriptionStore, handler);

        // Act
        await runner.RunOnceAsync("order-sub");

        // Assert - Only Order events
        handler.ProcessedEvents.Should().HaveCount(2);
        handler.ProcessedEvents.Should().AllBeOfType<TestOrderEvent>();
    }

    [Fact]
    public async Task Subscription_FiltersByEventType()
    {
        // Arrange
        var streamId = "Mixed-stream-1";
        await _eventStore.AppendAsync(streamId, [
            new TestOrderEvent { OrderId = "order-1", Amount = 100 },
            new TestCustomerEvent { CustomerId = "cust-1" },
            new TestOrderEvent { OrderId = "order-2", Amount = 200 }
        ]);

        var subscription = new PersistentSubscription("order-only-sub", "*")
        {
            EventTypeFilter = [typeof(TestOrderEvent).Name]
        };
        await _subscriptionStore.SaveAsync(subscription);

        var handler = new TestEventHandler();
        var runner = new SubscriptionRunner(_eventStore, _subscriptionStore, handler);

        // Act
        await runner.RunOnceAsync("order-only-sub");

        // Assert - Only TestOrderEvent
        handler.ProcessedEvents.Should().HaveCount(2);
        handler.ProcessedEvents.Should().AllBeOfType<TestOrderEvent>();
    }

    #endregion

    #region 4. Subscription management

    [Fact]
    public async Task SubscriptionStore_ListsAllSubscriptions()
    {
        // Arrange
        await _subscriptionStore.SaveAsync(new PersistentSubscription("sub-1", "Order-*"));
        await _subscriptionStore.SaveAsync(new PersistentSubscription("sub-2", "Customer-*"));
        await _subscriptionStore.SaveAsync(new PersistentSubscription("sub-3", "*"));

        // Act
        var subscriptions = await _subscriptionStore.ListAsync();

        // Assert
        subscriptions.Should().HaveCount(3);
        subscriptions.Select(s => s.Name).Should().Contain(["sub-1", "sub-2", "sub-3"]);
    }

    [Fact]
    public async Task SubscriptionStore_DeletesSubscription()
    {
        // Arrange
        await _subscriptionStore.SaveAsync(new PersistentSubscription("to-delete", "Order-*"));

        // Act
        await _subscriptionStore.DeleteAsync("to-delete");

        // Assert
        var loaded = await _subscriptionStore.LoadAsync("to-delete");
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task Subscription_TracksStatistics()
    {
        // Arrange
        var streamId = "Order-order-1";
        await _eventStore.AppendAsync(streamId, [
            new TestOrderEvent { OrderId = "order-1", Amount = 100 },
            new TestOrderEvent { OrderId = "order-2", Amount = 200 }
        ]);

        var subscription = new PersistentSubscription("stats-sub", "Order-*");
        await _subscriptionStore.SaveAsync(subscription);

        var handler = new TestEventHandler();
        var runner = new SubscriptionRunner(_eventStore, _subscriptionStore, handler);

        // Act
        await runner.RunOnceAsync("stats-sub");

        // Assert
        var updated = await _subscriptionStore.LoadAsync("stats-sub");
        updated!.ProcessedCount.Should().Be(2);
        updated.LastProcessedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    #endregion

    #region Test helpers

    private record TestOrderEvent : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
        public string OrderId { get; init; } = "";
        public decimal Amount { get; init; }
    }

    private record TestCustomerEvent : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
        public string CustomerId { get; init; } = "";
    }

    private class TestEventHandler : IEventHandler
    {
        public List<IEvent> ProcessedEvents { get; } = [];

        public ValueTask HandleAsync(IEvent @event, CancellationToken ct = default)
        {
            ProcessedEvents.Add(@event);
            return ValueTask.CompletedTask;
        }
    }

    #endregion
}
