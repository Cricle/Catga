using Catga.EventSourcing;
using FluentAssertions;

namespace Catga.Tests.EventSourcing;

public class EventStreamTests
{
    [Fact]
    public void EventStream_DefaultValues()
    {
        var stream = new EventStream();

        stream.StreamId.Should().BeEmpty();
        stream.Events.Should().BeEmpty();
        stream.Version.Should().Be(0);
    }

    [Fact]
    public void EventStream_CanSetStreamId()
    {
        var stream = new EventStream { StreamId = "order-123" };
        stream.StreamId.Should().Be("order-123");
    }

    [Fact]
    public void EventStream_CanSetEvents()
    {
        var events = new[]
        {
            new StoredEvent { Version = 0, EventType = "Created" },
            new StoredEvent { Version = 1, EventType = "Updated" }
        };
        var stream = new EventStream { Events = events };

        stream.Events.Should().HaveCount(2);
    }

    [Fact]
    public void EventStream_CanSetVersion()
    {
        var stream = new EventStream { Version = 5 };
        stream.Version.Should().Be(5);
    }

    [Fact]
    public void EventStream_EmptyEvents_IsValid()
    {
        var stream = new EventStream
        {
            StreamId = "empty-stream",
            Events = Array.Empty<StoredEvent>(),
            Version = -1
        };

        stream.Events.Should().BeEmpty();
        stream.Version.Should().Be(-1);
    }
}
