using Catga.Outbox;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Tests for OutboxMessage
/// </summary>
public class OutboxMessageTests
{
    [Fact]
    public void OutboxMessage_ShouldBeCreatable()
    {
        // Act
        var message = new OutboxMessage
        {
            MessageId = 12345L,
            MessageType = "TestMessage",
            Payload = new byte[] { 1, 2, 3 }
        };

        // Assert
        message.MessageId.Should().Be(12345L);
        message.MessageType.Should().Be("TestMessage");
        message.Payload.Should().HaveCount(3);
    }

    [Fact]
    public void OutboxMessage_Status_ShouldDefaultToPending()
    {
        // Act
        var message = new OutboxMessage
        {
            MessageId = 12345L,
            MessageType = "TestMessage",
            Payload = new byte[] { 1, 2, 3 }
        };

        // Assert
        message.Status.Should().Be(OutboxStatus.Pending);
    }

    [Fact]
    public void OutboxMessage_CanSetStatus_ToPublished()
    {
        // Arrange
        var message = new OutboxMessage
        {
            MessageId = 12345L,
            MessageType = "TestMessage",
            Payload = new byte[] { 1, 2, 3 }
        };

        // Act
        message.Status = OutboxStatus.Published;
        message.PublishedAt = DateTime.UtcNow;

        // Assert
        message.Status.Should().Be(OutboxStatus.Published);
        message.PublishedAt.Should().NotBeNull();
    }

    [Fact]
    public void OutboxMessage_CanSetStatus_ToFailed()
    {
        // Arrange
        var message = new OutboxMessage
        {
            MessageId = 12345L,
            MessageType = "TestMessage",
            Payload = new byte[] { 1, 2, 3 }
        };

        // Act
        message.Status = OutboxStatus.Failed;
        message.LastError = "Test error";

        // Assert
        message.Status.Should().Be(OutboxStatus.Failed);
        message.LastError.Should().Be("Test error");
    }

    [Fact]
    public void OutboxMessage_CanSetStatus_ToProcessing()
    {
        // Arrange
        var message = new OutboxMessage
        {
            MessageId = 12345L,
            MessageType = "TestMessage",
            Payload = new byte[] { 1, 2, 3 }
        };

        // Act
        message.Status = OutboxStatus.Processing;

        // Assert
        message.Status.Should().Be(OutboxStatus.Processing);
    }

    [Fact]
    public void OutboxMessage_RetryCount_ShouldBeIncrementable()
    {
        // Arrange
        var message = new OutboxMessage
        {
            MessageId = 12345L,
            MessageType = "TestMessage",
            Payload = new byte[] { 1, 2, 3 }
        };

        // Act
        message.RetryCount++;
        message.RetryCount++;

        // Assert
        message.RetryCount.Should().Be(2);
    }

    [Fact]
    public void OutboxMessage_CorrelationId_ShouldBeSettable()
    {
        // Act
        var message = new OutboxMessage
        {
            MessageId = 12345L,
            MessageType = "TestMessage",
            Payload = new byte[] { 1, 2, 3 },
            CorrelationId = 67890L
        };

        // Assert
        message.CorrelationId.Should().Be(67890L);
    }

    [Fact]
    public void OutboxMessage_MaxRetries_ShouldDefaultTo3()
    {
        // Act
        var message = new OutboxMessage
        {
            MessageId = 12345L,
            MessageType = "TestMessage",
            Payload = new byte[] { 1, 2, 3 }
        };

        // Assert
        message.MaxRetries.Should().Be(3);
    }

    [Fact]
    public void OutboxMessage_Metadata_ShouldBeSettable()
    {
        // Act
        var message = new OutboxMessage
        {
            MessageId = 12345L,
            MessageType = "TestMessage",
            Payload = new byte[] { 1, 2, 3 },
            Metadata = "{\"key\":\"value\"}"
        };

        // Assert
        message.Metadata.Should().Be("{\"key\":\"value\"}");
    }

    [Fact]
    public void OutboxStatus_ShouldHaveExpectedValues()
    {
        // Assert
        OutboxStatus.Pending.Should().BeDefined();
        OutboxStatus.Published.Should().BeDefined();
        OutboxStatus.Failed.Should().BeDefined();
        OutboxStatus.Processing.Should().BeDefined();
    }
}
