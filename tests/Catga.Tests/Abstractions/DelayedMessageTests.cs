using Catga.Abstractions;
using FluentAssertions;
using MemoryPack;
using Xunit;

namespace Catga.Tests.Abstractions;

public partial class DelayedMessageTests
{
    [Fact]
    public void IDelayedMessage_DeliverAt_UsesScheduledAt_WhenSet()
    {
        var scheduledAt = DateTimeOffset.UtcNow.AddHours(1);
        var message = new TestDelayedMessage
        {
            ScheduledAt = scheduledAt,
            Delay = TimeSpan.FromMinutes(30) // Should be ignored
        };

        message.DeliverAt.Should().Be(scheduledAt);
    }

    [Fact]
    public void IDelayedMessage_DeliverAt_UsesDelay_WhenScheduledAtNotSet()
    {
        var delay = TimeSpan.FromMinutes(30);
        var before = DateTimeOffset.UtcNow;
        var message = new TestDelayedMessage
        {
            Delay = delay
        };
        var after = DateTimeOffset.UtcNow;

        message.DeliverAt.Should().BeOnOrAfter(before.Add(delay));
        message.DeliverAt.Should().BeOnOrBefore(after.Add(delay).AddSeconds(1));
    }

    [Fact]
    public void IDelayedMessage_DeliverAt_ReturnsNow_WhenNeitherSet()
    {
        var before = DateTimeOffset.UtcNow;
        var message = new TestDelayedMessage();
        var after = DateTimeOffset.UtcNow;

        message.DeliverAt.Should().BeOnOrAfter(before);
        message.DeliverAt.Should().BeOnOrBefore(after.AddSeconds(1));
    }

    [Fact]
    public void MessagePriority_HasExpectedValues()
    {
        ((byte)MessagePriority.Low).Should().Be(0);
        ((byte)MessagePriority.Normal).Should().Be(1);
        ((byte)MessagePriority.High).Should().Be(2);
        ((byte)MessagePriority.Critical).Should().Be(3);
    }

    [Fact]
    public void IPrioritizedMessage_DefaultPriority_IsNormal()
    {
        var message = new TestPrioritizedMessage();

        message.Priority.Should().Be(MessagePriority.Normal);
    }

    [Fact]
    public void IPrioritizedMessage_CanSetPriority()
    {
        var message = new TestPrioritizedMessage { Priority = MessagePriority.Critical };

        message.Priority.Should().Be(MessagePriority.Critical);
    }

    [Fact]
    public void IDelayedRequest_InheritsFromBothInterfaces()
    {
        var message = new TestDelayedRequest
        {
            Delay = TimeSpan.FromMinutes(5)
        };

        message.Should().BeAssignableTo<IRequest<string>>();
        message.Should().BeAssignableTo<IDelayedMessage>();
        message.Delay.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void IDelayedEvent_InheritsFromBothInterfaces()
    {
        var message = new TestDelayedEvent
        {
            ScheduledAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        message.Should().BeAssignableTo<IEvent>();
        message.Should().BeAssignableTo<IDelayedMessage>();
        message.ScheduledAt.Should().NotBeNull();
    }

    // Test implementations
    [MemoryPackable]
    private partial record TestDelayedMessage : IDelayedMessage
    {
        public long MessageId { get; init; }
        public DateTimeOffset? ScheduledAt { get; init; }
        public TimeSpan? Delay { get; init; }
        public DateTimeOffset DeliverAt => ScheduledAt ?? (Delay.HasValue ? DateTimeOffset.UtcNow.Add(Delay.Value) : DateTimeOffset.UtcNow);
    }

    [MemoryPackable]
    private partial record TestPrioritizedMessage : IPrioritizedMessage
    {
        public long MessageId { get; init; }
        public MessagePriority Priority { get; init; } = MessagePriority.Normal;
    }

    [MemoryPackable]
    private partial record TestDelayedRequest : IDelayedRequest<string>
    {
        public long MessageId { get; init; }
        public DateTimeOffset? ScheduledAt { get; init; }
        public TimeSpan? Delay { get; init; }
        public DateTimeOffset DeliverAt => ScheduledAt ?? (Delay.HasValue ? DateTimeOffset.UtcNow.Add(Delay.Value) : DateTimeOffset.UtcNow);
    }

    [MemoryPackable]
    private partial record TestDelayedEvent : IDelayedEvent
    {
        public long MessageId { get; init; }
        public DateTimeOffset? ScheduledAt { get; init; }
        public TimeSpan? Delay { get; init; }
        public DateTimeOffset DeliverAt => ScheduledAt ?? (Delay.HasValue ? DateTimeOffset.UtcNow.Add(Delay.Value) : DateTimeOffset.UtcNow);
    }
}
