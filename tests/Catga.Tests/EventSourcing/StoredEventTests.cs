using Catga.Abstractions;
using Catga.Core;
using Catga.EventSourcing;
using FluentAssertions;

namespace Catga.Tests.EventSourcing;

public class StoredEventTests
{
    [Fact]
    public void StoredEvent_CanBeCreated()
    {
        var storedEvent = new StoredEvent();
        storedEvent.Should().NotBeNull();
    }

    [Fact]
    public void StoredEvent_CanSetVersion()
    {
        var storedEvent = new StoredEvent { Version = 10 };
        storedEvent.Version.Should().Be(10);
    }

    [Fact]
    public void StoredEvent_CanSetTimestamp()
    {
        var timestamp = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var storedEvent = new StoredEvent { Timestamp = timestamp };
        storedEvent.Timestamp.Should().Be(timestamp);
    }

    [Fact]
    public void StoredEvent_CanSetEventType()
    {
        var storedEvent = new StoredEvent { EventType = "OrderCreated" };
        storedEvent.EventType.Should().Be("OrderCreated");
    }

    [Fact]
    public void StoredEvent_CanSetEvent()
    {
        var evt = new TestEvent("test-data");
        var storedEvent = new StoredEvent { Event = evt };
        storedEvent.Event.Should().Be(evt);
    }

    private record TestEvent(string Data) : IEvent
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }
}
