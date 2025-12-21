using Catga.DeadLetter;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Tests for DeadLetterMessage
/// </summary>
public class DeadLetterMessageTests
{
    [Fact]
    public void DeadLetterMessage_ShouldBeCreatable()
    {
        // Act
        var message = new DeadLetterMessage
        {
            MessageId = 12345L,
            MessageType = "TestMessage",
            Message = "{\"test\":\"value\"}",
            ExceptionType = "System.InvalidOperationException",
            ExceptionMessage = "Test exception",
            StackTrace = "at Test.Method()"
        };

        // Assert
        message.MessageId.Should().Be(12345L);
        message.MessageType.Should().Be("TestMessage");
        message.Message.Should().Be("{\"test\":\"value\"}");
    }

    [Fact]
    public void DeadLetterMessage_ExceptionInfo_ShouldBeSettable()
    {
        // Act
        var message = new DeadLetterMessage
        {
            MessageId = 12345L,
            MessageType = "TestMessage",
            Message = "{}",
            ExceptionType = "System.InvalidOperationException",
            ExceptionMessage = "Test exception",
            StackTrace = "at Test.Method()\n   at Test.Caller()"
        };

        // Assert
        message.ExceptionType.Should().Be("System.InvalidOperationException");
        message.ExceptionMessage.Should().Be("Test exception");
        message.StackTrace.Should().Contain("Test.Method");
    }

    [Fact]
    public void DeadLetterMessage_RetryCount_ShouldBeSettable()
    {
        // Act
        var message = new DeadLetterMessage
        {
            MessageId = 12345L,
            MessageType = "TestMessage",
            Message = "{}",
            ExceptionType = "System.Exception",
            ExceptionMessage = "Error",
            StackTrace = "",
            RetryCount = 5
        };

        // Assert
        message.RetryCount.Should().Be(5);
    }

    [Fact]
    public void DeadLetterMessage_FailedAt_ShouldBeSettable()
    {
        // Arrange
        var failedTime = DateTime.UtcNow;

        // Act
        var message = new DeadLetterMessage
        {
            MessageId = 12345L,
            MessageType = "TestMessage",
            Message = "{}",
            ExceptionType = "System.Exception",
            ExceptionMessage = "Error",
            StackTrace = "",
            FailedAt = failedTime
        };

        // Assert
        message.FailedAt.Should().Be(failedTime);
    }

    [Fact]
    public void DeadLetterMessage_IsStruct_ShouldBeValueType()
    {
        // Assert
        typeof(DeadLetterMessage).IsValueType.Should().BeTrue();
    }

    [Fact]
    public void DeadLetterMessage_DefaultValues_ShouldWork()
    {
        // Act
        var message = new DeadLetterMessage
        {
            MessageId = 12345L,
            MessageType = "TestMessage",
            Message = "{}",
            ExceptionType = "System.Exception",
            ExceptionMessage = "Error",
            StackTrace = ""
        };

        // Assert
        message.RetryCount.Should().Be(0);
        message.FailedAt.Should().Be(default);
    }
}






