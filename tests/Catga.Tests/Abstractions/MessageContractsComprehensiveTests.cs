using Catga.Abstractions;
using Catga.Core;
using FluentAssertions;

namespace Catga.Tests.Abstractions;

/// <summary>
/// Comprehensive tests for MessageContracts - EventBase, ReliableEventBase, CommandBase, QueryBase
/// </summary>
public class MessageContractsComprehensiveTests
{
    #region EventBase Tests

    public record TestEvent : EventBase
    {
        public string Data { get; init; } = "";
    }

    [Fact]
    public void EventBase_DefaultValues_ShouldBeCorrect()
    {
        var evt = new TestEvent { MessageId = 123 };
        
        evt.MessageId.Should().Be(123);
        evt.CorrelationId.Should().BeNull();
        evt.CausationId.Should().BeNull();
        evt.OccurredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void EventBase_WithAllProperties_ShouldSetCorrectly()
    {
        var occurredAt = DateTime.UtcNow.AddMinutes(-5);
        var evt = new TestEvent
        {
            MessageId = 456,
            CorrelationId = 789,
            CausationId = 101112,
            OccurredAt = occurredAt,
            Data = "test"
        };
        
        evt.MessageId.Should().Be(456);
        evt.CorrelationId.Should().Be(789);
        evt.CausationId.Should().Be(101112);
        evt.OccurredAt.Should().Be(occurredAt);
        evt.Data.Should().Be("test");
    }

    [Fact]
    public void EventBase_QoS_ShouldBeAtMostOnce()
    {
        var evt = new TestEvent { MessageId = 1 };
        ((IEvent)evt).QoS.Should().Be(QualityOfService.AtMostOnce);
    }

    #endregion

    #region ReliableEventBase Tests

    public record TestReliableEvent : ReliableEventBase
    {
        public int Value { get; init; }
    }

    [Fact]
    public void ReliableEventBase_DefaultValues_ShouldBeCorrect()
    {
        var evt = new TestReliableEvent { MessageId = 123 };
        
        evt.MessageId.Should().Be(123);
        evt.CorrelationId.Should().BeNull();
        evt.CausationId.Should().BeNull();
        evt.OccurredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ReliableEventBase_QoS_ShouldBeAtLeastOnce()
    {
        var evt = new TestReliableEvent { MessageId = 1 };
        ((IReliableEvent)evt).QoS.Should().Be(QualityOfService.AtLeastOnce);
    }

    [Fact]
    public void ReliableEventBase_WithAllProperties_ShouldSetCorrectly()
    {
        var evt = new TestReliableEvent
        {
            MessageId = 100,
            CorrelationId = 200,
            CausationId = 300,
            Value = 42
        };
        
        evt.MessageId.Should().Be(100);
        evt.CorrelationId.Should().Be(200);
        evt.CausationId.Should().Be(300);
        evt.Value.Should().Be(42);
    }

    #endregion

    #region CommandBase Tests

    public record TestCommand : CommandBase
    {
        public string Name { get; init; } = "";
    }

    [Fact]
    public void CommandBase_DefaultValues_ShouldBeCorrect()
    {
        var cmd = new TestCommand { MessageId = 123 };
        
        cmd.MessageId.Should().Be(123);
        cmd.CorrelationId.Should().BeNull();
    }

    [Fact]
    public void CommandBase_WithCorrelationId_ShouldSetCorrectly()
    {
        var cmd = new TestCommand
        {
            MessageId = 456,
            CorrelationId = 789,
            Name = "test"
        };
        
        cmd.MessageId.Should().Be(456);
        cmd.CorrelationId.Should().Be(789);
        cmd.Name.Should().Be("test");
    }

    [Fact]
    public void CommandBase_ImplementsIRequest_ShouldBeTrue()
    {
        var cmd = new TestCommand { MessageId = 1 };
        cmd.Should().BeAssignableTo<IRequest>();
        cmd.Should().BeAssignableTo<IMessage>();
    }

    #endregion

    #region QueryBase Tests

    public record TestQuery : QueryBase<string>
    {
        public int Id { get; init; }
    }

    [Fact]
    public void QueryBase_DefaultValues_ShouldBeCorrect()
    {
        var query = new TestQuery { MessageId = 123 };
        
        query.MessageId.Should().Be(123);
        query.CorrelationId.Should().BeNull();
    }

    [Fact]
    public void QueryBase_WithAllProperties_ShouldSetCorrectly()
    {
        var query = new TestQuery
        {
            MessageId = 456,
            CorrelationId = 789,
            Id = 42
        };
        
        query.MessageId.Should().Be(456);
        query.CorrelationId.Should().Be(789);
        query.Id.Should().Be(42);
    }

    [Fact]
    public void QueryBase_ImplementsIRequestWithResponse_ShouldBeTrue()
    {
        var query = new TestQuery { MessageId = 1 };
        query.Should().BeAssignableTo<IRequest<string>>();
        query.Should().BeAssignableTo<IMessage>();
    }

    #endregion

    #region IMessage Default Values Tests

    [Fact]
    public void IMessage_DefaultQoS_ShouldBeAtLeastOnce()
    {
        var cmd = new TestCommand { MessageId = 1 };
        ((IMessage)cmd).QoS.Should().Be(QualityOfService.AtLeastOnce);
    }

    [Fact]
    public void IMessage_DefaultDeliveryMode_ShouldBeWaitForResult()
    {
        var cmd = new TestCommand { MessageId = 1 };
        ((IMessage)cmd).DeliveryMode.Should().Be(DeliveryMode.WaitForResult);
    }

    [Fact]
    public void IMessage_DefaultCorrelationId_ShouldBeNull()
    {
        var cmd = new TestCommand { MessageId = 1 };
        ((IMessage)cmd).CorrelationId.Should().BeNull();
    }

    #endregion

    #region MessagePriority Tests

    [Fact]
    public void MessagePriority_Values_ShouldBeCorrect()
    {
        ((byte)MessagePriority.Low).Should().Be(0);
        ((byte)MessagePriority.Normal).Should().Be(1);
        ((byte)MessagePriority.High).Should().Be(2);
        ((byte)MessagePriority.Critical).Should().Be(3);
    }

    #endregion

    #region IDelayedMessage Tests

    public record TestDelayedRequest : IDelayedRequest<string>
    {
        public long MessageId { get; init; }
        public DateTimeOffset? ScheduledAt { get; init; }
        public TimeSpan? Delay { get; init; }
    }

    [Fact]
    public void IDelayedMessage_WithScheduledAt_ShouldUseScheduledAt()
    {
        var scheduledAt = DateTimeOffset.UtcNow.AddHours(1);
        IDelayedMessage request = new TestDelayedRequest
        {
            MessageId = 1,
            ScheduledAt = scheduledAt,
            Delay = TimeSpan.FromMinutes(5) // Should be ignored
        };
        
        request.DeliverAt.Should().Be(scheduledAt);
    }

    [Fact]
    public void IDelayedMessage_WithDelayOnly_ShouldCalculateDeliverAt()
    {
        var delay = TimeSpan.FromMinutes(10);
        IDelayedMessage request = new TestDelayedRequest
        {
            MessageId = 1,
            Delay = delay
        };
        
        var expectedDeliverAt = DateTimeOffset.UtcNow.Add(delay);
        request.DeliverAt.Should().BeCloseTo(expectedDeliverAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void IDelayedMessage_WithNoSchedule_ShouldDeliverNow()
    {
        IDelayedMessage request = new TestDelayedRequest { MessageId = 1 };
        
        request.DeliverAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    #endregion
}
