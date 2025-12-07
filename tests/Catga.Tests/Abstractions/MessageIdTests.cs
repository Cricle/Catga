using Catga.Abstractions;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Abstractions;

/// <summary>
/// Unit tests for IMessage interface implementations.
/// </summary>
public class MessageTests
{
    [Fact]
    public void IRequest_HasMessageId()
    {
        // Arrange
        var request = new TestRequest { MessageId = 123 };

        // Assert
        request.MessageId.Should().Be(123);
    }

    [Fact]
    public void IEvent_HasMessageId()
    {
        // Arrange
        var evt = new TestEvent { MessageId = 456 };

        // Assert
        evt.MessageId.Should().Be(456);
    }

    [Fact]
    public void IRequest_IsIMessage()
    {
        // Arrange
        var request = new TestRequest { MessageId = 1 };

        // Assert
        request.Should().BeAssignableTo<IMessage>();
    }

    [Fact]
    public void IEvent_IsIMessage()
    {
        // Arrange
        var evt = new TestEvent { MessageId = 1 };

        // Assert
        evt.Should().BeAssignableTo<IMessage>();
    }

    [Fact]
    public void IRequest_WithResponse_HasCorrectType()
    {
        // Arrange
        var request = new TestRequestWithResponse { MessageId = 1 };

        // Assert
        request.Should().BeAssignableTo<IRequest<int>>();
    }

    private record TestRequest : IRequest<string>
    {
        public long MessageId { get; init; }
    }

    private record TestEvent : IEvent
    {
        public long MessageId { get; init; }
    }

    private record TestRequestWithResponse : IRequest<int>
    {
        public long MessageId { get; init; }
    }
}
