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
