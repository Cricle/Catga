using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.Stores;
using Catga.Resilience;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

public class TimeTravelTests
{
    private readonly InMemoryEventStore _eventStore;

    public TimeTravelTests()
    {
        _eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
    }

    [Fact]
    public async Task ReadToVersionAsync_ReturnsEventsUpToVersion()
    {
        // Arrange
        var streamId = "test-stream-1";
        var events = new IEvent[]
        {
            new TestEvent("Event1"),
            new TestEvent("Event2"),
            new TestEvent("Event3"),
            new TestEvent("Event4"),
            new TestEvent("Event5")
        };
        await _eventStore.AppendAsync(streamId, events);

        // Act
        var result = await _eventStore.ReadToVersionAsync(streamId, 2);

        // Assert
        result.Events.Should().HaveCount(3); // versions 0, 1, 2
        result.Version.Should().Be(2);
        result.Events[0].Event.Should().BeOfType<TestEvent>().Which.Name.Should().Be("Event1");
        result.Events[1].Event.Should().BeOfType<TestEvent>().Which.Name.Should().Be("Event2");
        result.Events[2].Event.Should().BeOfType<TestEvent>().Which.Name.Should().Be("Event3");
    }

    [Fact]
    public async Task ReadToVersionAsync_ReturnsEmptyForNonExistentStream()
    {
        // Act
        var result = await _eventStore.ReadToVersionAsync("non-existent", 5);

        // Assert
        result.Events.Should().BeEmpty();
        result.Version.Should().Be(-1);
    }

    [Fact]
    public async Task ReadToTimestampAsync_ReturnsEventsUpToTimestamp()
    {
        // Arrange
        var streamId = "test-stream-2";
        var events = new IEvent[] { new TestEvent("Event1") };
        await _eventStore.AppendAsync(streamId, events);

        var afterFirstEvent = DateTime.UtcNow;
        await Task.Delay(50); // Small delay to ensure different timestamps

        var moreEvents = new IEvent[] { new TestEvent("Event2"), new TestEvent("Event3") };
        await _eventStore.AppendAsync(streamId, moreEvents, 0);

        // Act - Get events up to the timestamp after first event
        var result = await _eventStore.ReadToTimestampAsync(streamId, afterFirstEvent);

        // Assert
        result.Events.Should().HaveCount(1);
        result.Events[0].Event.Should().BeOfType<TestEvent>().Which.Name.Should().Be("Event1");
    }

    [Fact]
    public async Task GetVersionHistoryAsync_ReturnsAllVersionInfo()
    {
        // Arrange
        var streamId = "test-stream-3";
        var events = new IEvent[]
        {
            new TestEvent("Event1"),
            new TestEvent("Event2"),
            new TestEvent("Event3")
        };
        await _eventStore.AppendAsync(streamId, events);

        // Act
        var history = await _eventStore.GetVersionHistoryAsync(streamId);

        // Assert
        history.Should().HaveCount(3);
        history[0].Version.Should().Be(0);
        history[0].EventType.Should().Be("TestEvent");
        history[1].Version.Should().Be(1);
        history[2].Version.Should().Be(2);
    }

    [Fact]
    public async Task GetVersionHistoryAsync_ReturnsEmptyForNonExistentStream()
    {
        // Act
        var history = await _eventStore.GetVersionHistoryAsync("non-existent");

        // Assert
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task TimeTravelService_GetStateAtVersion_ReconstructsCorrectState()
    {
        // Arrange
        var streamId = "TestAggregate-order-1";
        var events = new IEvent[]
        {
            new OrderCreatedEvent { OrderId = "order-1", CustomerId = "customer-1" },
            new OrderItemAddedEvent { OrderId = "order-1", ItemName = "Item1", Price = 10m },
            new OrderItemAddedEvent { OrderId = "order-1", ItemName = "Item2", Price = 20m },
            new OrderConfirmedEvent { OrderId = "order-1" }
        };
        await _eventStore.AppendAsync(streamId, events);

        var timeTravelService = new TimeTravelService<TestAggregate>(_eventStore);

        // Act - Get state at version 1 (after OrderCreated and first ItemAdded)
        var stateAtV1 = await timeTravelService.GetStateAtVersionAsync("order-1", 1);

        // Assert
        stateAtV1.Should().NotBeNull();
        stateAtV1!.Id.Should().Be("order-1");
        stateAtV1.CustomerId.Should().Be("customer-1");
        stateAtV1.TotalAmount.Should().Be(10m); // Only first item
        stateAtV1.IsConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task TimeTravelService_CompareVersions_ShowsDifference()
    {
        // Arrange
        var streamId = "TestAggregate-order-2";
        var events = new IEvent[]
        {
            new OrderCreatedEvent { OrderId = "order-2", CustomerId = "customer-2" },
            new OrderItemAddedEvent { OrderId = "order-2", ItemName = "Item1", Price = 100m },
            new OrderConfirmedEvent { OrderId = "order-2" }
        };
        await _eventStore.AppendAsync(streamId, events);

        var timeTravelService = new TimeTravelService<TestAggregate>(_eventStore);

        // Act
        var comparison = await timeTravelService.CompareVersionsAsync("order-2", 0, 2);

        // Assert
        comparison.FromVersion.Should().Be(0);
        comparison.ToVersion.Should().Be(2);
        comparison.FromState!.IsConfirmed.Should().BeFalse();
        comparison.ToState!.IsConfirmed.Should().BeTrue();
        comparison.EventsBetween.Should().HaveCount(2); // ItemAdded and Confirmed
    }

    #region Additional Unit Tests

    [Fact]
    public async Task ReadToVersionAsync_WithVersionZero_ReturnsSingleEvent()
    {
        // Arrange
        var streamId = "test-stream-v0";
        var events = new IEvent[]
        {
            new TestEvent("Event1"),
            new TestEvent("Event2"),
            new TestEvent("Event3")
        };
        await _eventStore.AppendAsync(streamId, events);

        // Act
        var result = await _eventStore.ReadToVersionAsync(streamId, 0);

        // Assert
        result.Events.Should().HaveCount(1);
        result.Version.Should().Be(0);
        result.Events[0].Event.Should().BeOfType<TestEvent>().Which.Name.Should().Be("Event1");
    }

    [Fact]
    public async Task ReadToVersionAsync_WithVersionBeyondMax_ReturnsAllEvents()
    {
        // Arrange
        var streamId = "test-stream-beyond";
        var events = new IEvent[]
        {
            new TestEvent("Event1"),
            new TestEvent("Event2")
        };
        await _eventStore.AppendAsync(streamId, events);

        // Act
        var result = await _eventStore.ReadToVersionAsync(streamId, 100);

        // Assert
        result.Events.Should().HaveCount(2);
        result.Version.Should().Be(1); // Actual max version
    }

    [Fact]
    public async Task ReadToTimestampAsync_WithFutureTimestamp_ReturnsAllEvents()
    {
        // Arrange
        var streamId = "test-stream-future";
        var events = new IEvent[]
        {
            new TestEvent("Event1"),
            new TestEvent("Event2")
        };
        await _eventStore.AppendAsync(streamId, events);

        // Act
        var result = await _eventStore.ReadToTimestampAsync(streamId, DateTime.UtcNow.AddHours(1));

        // Assert
        result.Events.Should().HaveCount(2);
    }

    [Fact]
    public async Task ReadToTimestampAsync_WithPastTimestamp_ReturnsNoEvents()
    {
        // Arrange
        var pastTime = DateTime.UtcNow.AddDays(-1);
        var streamId = "test-stream-past";
        var events = new IEvent[] { new TestEvent("Event1") };
        await _eventStore.AppendAsync(streamId, events);

        // Act
        var result = await _eventStore.ReadToTimestampAsync(streamId, pastTime);

        // Assert
        result.Events.Should().BeEmpty();
        result.Version.Should().Be(-1);
    }

    [Fact]
    public async Task GetVersionHistoryAsync_ContainsCorrectTimestamps()
    {
        // Arrange
        var streamId = "test-stream-timestamps";
        var beforeAppend = DateTime.UtcNow;
        var events = new IEvent[] { new TestEvent("Event1") };
        await _eventStore.AppendAsync(streamId, events);
        var afterAppend = DateTime.UtcNow;

        // Act
        var history = await _eventStore.GetVersionHistoryAsync(streamId);

        // Assert
        history.Should().HaveCount(1);
        history[0].Timestamp.Should().BeOnOrAfter(beforeAppend);
        history[0].Timestamp.Should().BeOnOrBefore(afterAppend);
    }

    [Fact]
    public async Task TimeTravelService_GetStateAtVersion_ReturnsNullForNonExistentAggregate()
    {
        // Arrange
        var timeTravelService = new TimeTravelService<TestAggregate>(_eventStore);

        // Act
        var state = await timeTravelService.GetStateAtVersionAsync("non-existent-order", 0);

        // Assert
        state.Should().BeNull();
    }

    [Fact]
    public async Task TimeTravelService_GetStateAtTimestamp_ReconstructsCorrectState()
    {
        // Arrange
        var streamId = "TestAggregate-order-ts";
        var events1 = new IEvent[]
        {
            new OrderCreatedEvent { OrderId = "order-ts", CustomerId = "customer-ts" }
        };
        await _eventStore.AppendAsync(streamId, events1);

        var afterCreate = DateTime.UtcNow;
        await Task.Delay(50);

        var events2 = new IEvent[]
        {
            new OrderItemAddedEvent { OrderId = "order-ts", ItemName = "Item1", Price = 50m }
        };
        await _eventStore.AppendAsync(streamId, events2, 0);

        var timeTravelService = new TimeTravelService<TestAggregate>(_eventStore);

        // Act - Get state at timestamp after create but before item added
        var stateAtCreate = await timeTravelService.GetStateAtTimestampAsync("order-ts", afterCreate);

        // Assert
        stateAtCreate.Should().NotBeNull();
        stateAtCreate!.Id.Should().Be("order-ts");
        stateAtCreate.TotalAmount.Should().Be(0m); // No items yet
    }

    [Fact]
    public async Task TimeTravelService_GetVersionHistory_ReturnsCorrectHistory()
    {
        // Arrange
        var streamId = "TestAggregate-order-hist";
        var events = new IEvent[]
        {
            new OrderCreatedEvent { OrderId = "order-hist", CustomerId = "customer-hist" },
            new OrderItemAddedEvent { OrderId = "order-hist", ItemName = "Item1", Price = 10m },
            new OrderConfirmedEvent { OrderId = "order-hist" }
        };
        await _eventStore.AppendAsync(streamId, events);

        var timeTravelService = new TimeTravelService<TestAggregate>(_eventStore);

        // Act
        var history = await timeTravelService.GetVersionHistoryAsync("order-hist");

        // Assert
        history.Should().HaveCount(3);
        history[0].EventType.Should().Be("OrderCreatedEvent");
        history[1].EventType.Should().Be("OrderItemAddedEvent");
        history[2].EventType.Should().Be("OrderConfirmedEvent");
    }

    [Fact]
    public async Task TimeTravelService_CompareVersions_WithSameVersion_ReturnsIdenticalStates()
    {
        // Arrange
        var streamId = "TestAggregate-order-same";
        var events = new IEvent[]
        {
            new OrderCreatedEvent { OrderId = "order-same", CustomerId = "customer-same" },
            new OrderItemAddedEvent { OrderId = "order-same", ItemName = "Item1", Price = 100m }
        };
        await _eventStore.AppendAsync(streamId, events);

        var timeTravelService = new TimeTravelService<TestAggregate>(_eventStore);

        // Act
        var comparison = await timeTravelService.CompareVersionsAsync("order-same", 1, 1);

        // Assert
        comparison.FromVersion.Should().Be(1);
        comparison.ToVersion.Should().Be(1);
        comparison.FromState!.TotalAmount.Should().Be(comparison.ToState!.TotalAmount);
        comparison.EventsBetween.Should().BeEmpty();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(500)]
    public async Task ReadToVersionAsync_WithManyEvents_PerformsCorrectly(int eventCount)
    {
        // Arrange
        var streamId = $"test-stream-many-{eventCount}";
        var events = Enumerable.Range(0, eventCount)
            .Select(i => new TestEvent($"Event{i}"))
            .Cast<IEvent>()
            .ToArray();
        await _eventStore.AppendAsync(streamId, events);

        var targetVersion = eventCount / 2;

        // Act
        var result = await _eventStore.ReadToVersionAsync(streamId, targetVersion);

        // Assert
        result.Events.Should().HaveCount(targetVersion + 1);
        result.Version.Should().Be(targetVersion);
    }

    [Fact]
    public async Task MultipleStreams_TimeTravelOperationsAreIsolated()
    {
        // Arrange
        var streamId1 = "test-stream-isolated-1";
        var streamId2 = "test-stream-isolated-2";

        await _eventStore.AppendAsync(streamId1, new IEvent[] { new TestEvent("Stream1Event1"), new TestEvent("Stream1Event2") });
        await _eventStore.AppendAsync(streamId2, new IEvent[] { new TestEvent("Stream2Event1") });

        // Act
        var result1 = await _eventStore.ReadToVersionAsync(streamId1, 0);
        var result2 = await _eventStore.ReadToVersionAsync(streamId2, 0);

        // Assert
        result1.Events.Should().HaveCount(1);
        result1.Events[0].Event.Should().BeOfType<TestEvent>().Which.Name.Should().Be("Stream1Event1");

        result2.Events.Should().HaveCount(1);
        result2.Events[0].Event.Should().BeOfType<TestEvent>().Which.Name.Should().Be("Stream2Event1");
    }

    [Fact]
    public async Task ConcurrentTimeTravelReads_DoNotInterfere()
    {
        // Arrange
        var streamId = "test-stream-concurrent";
        var events = Enumerable.Range(0, 100)
            .Select(i => new TestEvent($"Event{i}"))
            .Cast<IEvent>()
            .ToArray();
        await _eventStore.AppendAsync(streamId, events);

        // Act - Concurrent reads at different versions
        var tasks = Enumerable.Range(0, 10)
            .Select(i => _eventStore.ReadToVersionAsync(streamId, i * 10).AsTask())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert
        for (int i = 0; i < 10; i++)
        {
            results[i].Events.Should().HaveCount(i * 10 + 1);
            results[i].Version.Should().Be(i * 10);
        }
    }

    #endregion

    // Test event
    private record TestEvent(string Name) : IEvent
    {
        public long MessageId { get; init; } = Random.Shared.NextInt64();
        public long? CorrelationId { get; init; }
        public long? CausationId { get; init; }
    }

    // Test aggregate and events for time travel service tests
    private class TestAggregate : AggregateRoot
    {
        public override string Id { get; protected set; } = string.Empty;
        public string CustomerId { get; private set; } = string.Empty;
        public decimal TotalAmount { get; private set; }
        public bool IsConfirmed { get; private set; }

        protected override void When(IEvent @event)
        {
            switch (@event)
            {
                case OrderCreatedEvent e:
                    Id = e.OrderId;
                    CustomerId = e.CustomerId;
                    break;
                case OrderItemAddedEvent e:
                    TotalAmount += e.Price;
                    break;
                case OrderConfirmedEvent:
                    IsConfirmed = true;
                    break;
            }
        }
    }

    private record OrderCreatedEvent : IEvent
    {
        public long MessageId { get; init; } = Random.Shared.NextInt64();
        public long? CorrelationId { get; init; }
        public long? CausationId { get; init; }
        public required string OrderId { get; init; }
        public required string CustomerId { get; init; }
    }

    private record OrderItemAddedEvent : IEvent
    {
        public long MessageId { get; init; } = Random.Shared.NextInt64();
        public long? CorrelationId { get; init; }
        public long? CausationId { get; init; }
        public required string OrderId { get; init; }
        public required string ItemName { get; init; }
        public required decimal Price { get; init; }
    }

    private record OrderConfirmedEvent : IEvent
    {
        public long MessageId { get; init; } = Random.Shared.NextInt64();
        public long? CorrelationId { get; init; }
        public long? CausationId { get; init; }
        public required string OrderId { get; init; }
    }
}
