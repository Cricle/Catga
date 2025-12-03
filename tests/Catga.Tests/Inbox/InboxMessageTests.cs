using Catga.Inbox;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Inbox;

/// <summary>
/// Tests for InboxMessage
/// </summary>
public class InboxMessageTests
{
    [Fact]
    public void InboxMessage_ShouldBeCreatable()
    {
        // Act
        var message = new InboxMessage
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
    public void InboxMessage_Status_ShouldDefaultToPending()
    {
        // Act
        var message = new InboxMessage
        {
            MessageId = 12345L,
            MessageType = "TestMessage",
            Payload = new byte[] { 1, 2, 3 }
        };

        // Assert
        message.Status.Should().Be(InboxStatus.Pending);
    }

    [Fact]
    public void InboxMessage_CanSetStatus_ToProcessed()
    {
        // Arrange
        var message = new InboxMessage
        {
            MessageId = 12345L,
            MessageType = "TestMessage",
            Payload = new byte[] { 1, 2, 3 }
        };

        // Act
        message.Status = InboxStatus.Processed;
        message.ProcessedAt = DateTime.UtcNow;

        // Assert
        message.Status.Should().Be(InboxStatus.Processed);
        message.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public void InboxMessage_CanSetStatus_ToFailed()
    {
        // Arrange
        var message = new InboxMessage
        {
            MessageId = 12345L,
            MessageType = "TestMessage",
            Payload = new byte[] { 1, 2, 3 }
        };

        // Act
        message.Status = InboxStatus.Failed;

        // Assert
        message.Status.Should().Be(InboxStatus.Failed);
    }

    [Fact]
    public void InboxMessage_CanSetStatus_ToProcessing()
    {
        // Arrange
        var message = new InboxMessage
        {
            MessageId = 12345L,
            MessageType = "TestMessage",
            Payload = new byte[] { 1, 2, 3 }
        };

        // Act
        message.Status = InboxStatus.Processing;
        message.LockExpiresAt = DateTime.UtcNow.AddMinutes(5);

        // Assert
        message.Status.Should().Be(InboxStatus.Processing);
        message.LockExpiresAt.Should().NotBeNull();
    }

    [Fact]
    public void InboxMessage_ProcessingResult_ShouldBeSettable()
    {
        // Arrange
        var message = new InboxMessage
        {
            MessageId = 12345L,
            MessageType = "TestMessage",
            Payload = new byte[] { 1, 2, 3 }
        };

        // Act
        message.ProcessingResult = new byte[] { 4, 5, 6 };

        // Assert
        message.ProcessingResult.Should().HaveCount(3);
    }

    [Fact]
    public void InboxMessage_CorrelationId_ShouldBeSettable()
    {
        // Act
        var message = new InboxMessage
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
    public void InboxMessage_Metadata_ShouldBeSettable()
    {
        // Act
        var message = new InboxMessage
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
    public void InboxStatus_ShouldHaveExpectedValues()
    {
        // Assert
        InboxStatus.Pending.Should().BeDefined();
        InboxStatus.Processing.Should().BeDefined();
        InboxStatus.Processed.Should().BeDefined();
        InboxStatus.Failed.Should().BeDefined();
    }
}
