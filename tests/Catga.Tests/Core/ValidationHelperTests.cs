using Catga.Abstractions;
using Catga.Core;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// ValidationHelper单元测试
/// 目标覆盖率: 从 8.6% → 95%+
/// </summary>
public class ValidationHelperTests
{
    #region ValidateMessage Tests

    [Fact]
    public void ValidateMessage_WithValidMessage_ShouldNotThrow()
    {
        // Arrange
        var message = new TestMessage { MessageId = 123 };

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessage(message);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateMessage_WithNullMessage_ShouldThrowArgumentNullException()
    {
        // Arrange
        TestMessage? message = null;

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessage(message);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("message");
    }

    [Fact]
    public void ValidateMessage_WithZeroMessageId_ShouldThrowArgumentException()
    {
        // Arrange
        var message = new TestMessage { MessageId = 0 };

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessage(message);
        act.Should().Throw<ArgumentException>()
            .WithMessage("MessageId must be > 0*");
    }

    [Fact]
    public void ValidateMessage_WithNegativeMessageId_ShouldNotThrow()
    {
        // Arrange  - MessageId是long，可以是负数（虽然不推荐）
        var message = new TestMessage { MessageId = -1 };

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessage(message);
        act.Should().NotThrow(); // 仅检查是否为0
    }

    [Fact]
    public void ValidateMessage_WithNonIMessage_ShouldOnlyCheckNull()
    {
        // Arrange
        var nonMessage = new NonMessageClass();

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessage(nonMessage);
        act.Should().NotThrow(); // 不实现IMessage接口，只检查null
    }

    #endregion

    #region ValidateMessageId Tests

    [Fact]
    public void ValidateMessageId_WithPositiveId_ShouldNotThrow()
    {
        // Arrange
        long messageId = 123456;

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessageId(messageId);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateMessageId_WithZeroId_ShouldThrowArgumentException()
    {
        // Arrange
        long messageId = 0;

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessageId(messageId);
        act.Should().Throw<ArgumentException>()
            .WithMessage("MessageId must be > 0*");
    }

    [Fact]
    public void ValidateMessageId_WithNegativeId_ShouldNotThrow()
    {
        // Arrange - 负数ID在某些场景下可能有效
        long messageId = -999;

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessageId(messageId);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateMessageId_WithMaxValue_ShouldNotThrow()
    {
        // Arrange
        long messageId = long.MaxValue;

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessageId(messageId);
        act.Should().NotThrow();
    }

    #endregion

    #region ValidateMessages Tests

    [Fact]
    public void ValidateMessages_WithValidCollection_ShouldNotThrow()
    {
        // Arrange
        var messages = new List<TestMessage>
        {
            new() { MessageId = 1 },
            new() { MessageId = 2 }
        };

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessages(messages);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateMessages_WithNullCollection_ShouldThrowArgumentNullException()
    {
        // Arrange
        List<TestMessage>? messages = null;

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessages(messages);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("messages");
    }

    [Fact]
    public void ValidateMessages_WithEmptyList_ShouldThrowArgumentException()
    {
        // Arrange
        var messages = new List<TestMessage>();

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessages(messages);
        act.Should().Throw<ArgumentException>()
            .WithMessage("Messages collection cannot be empty*");
    }

    [Fact]
    public void ValidateMessages_WithEmptyArray_ShouldThrowArgumentException()
    {
        // Arrange
        var messages = Array.Empty<TestMessage>();

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessages(messages);
        act.Should().Throw<ArgumentException>()
            .WithMessage("Messages collection cannot be empty*");
    }

    [Fact]
    public void ValidateMessages_WithEmptyEnumerable_ShouldThrowArgumentException()
    {
        // Arrange
        var messages = Enumerable.Empty<TestMessage>();

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessages(messages);
        act.Should().Throw<ArgumentException>()
            .WithMessage("Messages collection cannot be empty*");
    }

    [Fact]
    public void ValidateMessages_WithNonEmptyEnumerable_ShouldNotThrow()
    {
        // Arrange
        var messages = new TestMessage[]
        {
            new() { MessageId = 1 }
        }.AsEnumerable();

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessages(messages);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateMessages_WithSingleItem_ShouldNotThrow()
    {
        // Arrange
        var messages = new List<TestMessage> { new() { MessageId = 1 } };

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessages(messages);
        act.Should().NotThrow();
    }

    #endregion

    #region ValidateNotNull Tests

    [Fact]
    public void ValidateNotNull_WithValidObject_ShouldNotThrow()
    {
        // Arrange
        var obj = new object();

        // Act & Assert
        var act = () => ValidationHelper.ValidateNotNull(obj);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateNotNull_WithNullObject_ShouldThrowArgumentNullException()
    {
        // Arrange
        object? obj = null;

        // Act & Assert
        var act = () => ValidationHelper.ValidateNotNull(obj);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("obj");
    }

    [Fact]
    public void ValidateNotNull_WithString_ShouldNotThrow()
    {
        // Arrange
        string value = "test";

        // Act & Assert
        var act = () => ValidationHelper.ValidateNotNull(value);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateNotNull_WithEmptyString_ShouldNotThrow()
    {
        // Arrange - ValidateNotNull只检查null，不检查空字符串
        string value = "";

        // Act & Assert
        var act = () => ValidationHelper.ValidateNotNull(value);
        act.Should().NotThrow();
    }

    #endregion

    #region ValidateNotNullOrEmpty Tests

    [Fact]
    public void ValidateNotNullOrEmpty_WithValidString_ShouldNotThrow()
    {
        // Arrange
        string value = "test";

        // Act & Assert
        var act = () => ValidationHelper.ValidateNotNullOrEmpty(value);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateNotNullOrEmpty_WithNullString_ShouldThrowArgumentException()
    {
        // Arrange
        string? value = null;

        // Act & Assert
        var act = () => ValidationHelper.ValidateNotNullOrEmpty(value);
        act.Should().Throw<ArgumentException>()
            .WithMessage("Value cannot be null or empty*");
    }

    [Fact]
    public void ValidateNotNullOrEmpty_WithEmptyString_ShouldThrowArgumentException()
    {
        // Arrange
        string value = "";

        // Act & Assert
        var act = () => ValidationHelper.ValidateNotNullOrEmpty(value);
        act.Should().Throw<ArgumentException>()
            .WithMessage("Value cannot be null or empty*");
    }

    [Fact]
    public void ValidateNotNullOrEmpty_WithWhiteSpace_ShouldNotThrow()
    {
        // Arrange - ValidateNotNullOrEmpty允许只有空格的字符串
        string value = "   ";

        // Act & Assert
        var act = () => ValidationHelper.ValidateNotNullOrEmpty(value);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateNotNullOrEmpty_WithSingleCharacter_ShouldNotThrow()
    {
        // Arrange
        string value = "a";

        // Act & Assert
        var act = () => ValidationHelper.ValidateNotNullOrEmpty(value);
        act.Should().NotThrow();
    }

    #endregion

    #region ValidateNotNullOrWhiteSpace Tests

    [Fact]
    public void ValidateNotNullOrWhiteSpace_WithValidString_ShouldNotThrow()
    {
        // Arrange
        string value = "test";

        // Act & Assert
        var act = () => ValidationHelper.ValidateNotNullOrWhiteSpace(value);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateNotNullOrWhiteSpace_WithNullString_ShouldThrowArgumentException()
    {
        // Arrange
        string? value = null;

        // Act & Assert
        var act = () => ValidationHelper.ValidateNotNullOrWhiteSpace(value);
        act.Should().Throw<ArgumentException>()
            .WithMessage("Value cannot be null, empty, or whitespace*");
    }

    [Fact]
    public void ValidateNotNullOrWhiteSpace_WithEmptyString_ShouldThrowArgumentException()
    {
        // Arrange
        string value = "";

        // Act & Assert
        var act = () => ValidationHelper.ValidateNotNullOrWhiteSpace(value);
        act.Should().Throw<ArgumentException>()
            .WithMessage("Value cannot be null, empty, or whitespace*");
    }

    [Fact]
    public void ValidateNotNullOrWhiteSpace_WithWhiteSpaceOnly_ShouldThrowArgumentException()
    {
        // Arrange
        string value = "   ";

        // Act & Assert
        var act = () => ValidationHelper.ValidateNotNullOrWhiteSpace(value);
        act.Should().Throw<ArgumentException>()
            .WithMessage("Value cannot be null, empty, or whitespace*");
    }

    [Fact]
    public void ValidateNotNullOrWhiteSpace_WithTabsAndNewlines_ShouldThrowArgumentException()
    {
        // Arrange
        string value = "\t\n\r";

        // Act & Assert
        var act = () => ValidationHelper.ValidateNotNullOrWhiteSpace(value);
        act.Should().Throw<ArgumentException>()
            .WithMessage("Value cannot be null, empty, or whitespace*");
    }

    [Fact]
    public void ValidateNotNullOrWhiteSpace_WithMixedWhitespace_ShouldThrowArgumentException()
    {
        // Arrange
        string value = " \t \n ";

        // Act & Assert
        var act = () => ValidationHelper.ValidateNotNullOrWhiteSpace(value);
        act.Should().Throw<ArgumentException>()
            .WithMessage("Value cannot be null, empty, or whitespace*");
    }

    [Fact]
    public void ValidateNotNullOrWhiteSpace_WithStringContainingWhitespace_ShouldNotThrow()
    {
        // Arrange - 包含空格但不全是空格的字符串应该通过
        string value = "hello world";

        // Act & Assert
        var act = () => ValidationHelper.ValidateNotNullOrWhiteSpace(value);
        act.Should().NotThrow();
    }

    #endregion

    #region Test Helper Classes

    private class TestMessage : IMessage
    {
        public long MessageId { get; init; }
        public long? CorrelationId { get; init; }
    }

    private class NonMessageClass
    {
        public string Name { get; set; } = "Test";
    }

    #endregion
}

