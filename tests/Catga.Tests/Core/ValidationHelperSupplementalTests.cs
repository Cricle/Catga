using Catga.Abstractions;
using Catga.Core;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// ValidationHelper补充测试 - 提升覆盖率从86.9%到95%+
/// 聚焦：边界情况、IEnumerable路径、CallerArgumentExpression
/// </summary>
public class ValidationHelperSupplementalTests
{
    // ==================== ValidateMessages IEnumerable Path ====================

    [Fact]
    public void ValidateMessages_WithIEnumerableEmpty_ShouldThrowArgumentException()
    {
        // Arrange - Create IEnumerable that is NOT ICollection
        var messages = EmptyEnumerable();

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessages(messages);
        act.Should().Throw<ArgumentException>()
            .WithMessage("Messages collection cannot be empty*");
    }

    [Fact]
    public void ValidateMessages_WithIEnumerableNonEmpty_ShouldNotThrow()
    {
        // Arrange - Create IEnumerable that is NOT ICollection
        var messages = NonEmptyEnumerable();

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessages(messages);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateMessages_WithLazyEnumerable_ShouldCheckFirstElement()
    {
        // Arrange - Lazy enumerable that yields items
        var messages = LazyEnumerable(3);

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessages(messages);
        act.Should().NotThrow();
    }

    // ==================== ValidateMessageId Edge Cases ====================

    [Fact]
    public void ValidateMessageId_WithNegativeId_ShouldNotThrow()
    {
        // Arrange - Negative IDs are valid (only 0 is invalid)
        long messageId = -123;

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessageId(messageId);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateMessageId_WithMaxLong_ShouldNotThrow()
    {
        // Arrange
        long messageId = long.MaxValue;

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessageId(messageId);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateMessageId_WithMinLong_ShouldNotThrow()
    {
        // Arrange
        long messageId = long.MinValue;

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessageId(messageId);
        act.Should().NotThrow();
    }

    // ==================== ValidateMessage Edge Cases ====================

    [Fact]
    public void ValidateMessage_WithNegativeMessageId_ShouldNotThrow()
    {
        // Arrange - Negative MessageIds are valid
        var message = new TestMessage { MessageId = -999 };

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessage(message);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateMessage_WithMaxLongMessageId_ShouldNotThrow()
    {
        // Arrange
        var message = new TestMessage { MessageId = long.MaxValue };

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessage(message);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateMessage_WithNonMessageClass_ShouldOnlyCheckNull()
    {
        // Arrange - Class that is NOT IMessage
        var nonMessage = new NonMessage { Data = "test" };

        // Act & Assert - Should only check for null, not MessageId
        var act = () => ValidationHelper.ValidateMessage(nonMessage);
        act.Should().NotThrow();
    }

    // ==================== ValidateNotNullOrEmpty Edge Cases ====================

    [Fact]
    public void ValidateNotNullOrEmpty_WithWhitespaceOnly_ShouldNotThrow()
    {
        // Arrange - Whitespace is not empty, so should pass
        string value = "   ";

        // Act & Assert
        var act = () => ValidationHelper.ValidateNotNullOrEmpty(value);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateNotNullOrEmpty_WithSingleSpace_ShouldNotThrow()
    {
        // Arrange
        string value = " ";

        // Act & Assert
        var act = () => ValidationHelper.ValidateNotNullOrEmpty(value);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateNotNullOrEmpty_WithTab_ShouldNotThrow()
    {
        // Arrange
        string value = "\t";

        // Act & Assert
        var act = () => ValidationHelper.ValidateNotNullOrEmpty(value);
        act.Should().NotThrow();
    }

    // ==================== ValidateNotNullOrWhiteSpace Edge Cases ====================

    [Fact]
    public void ValidateNotNullOrWhiteSpace_WithMixedWhitespace_ShouldThrow()
    {
        // Arrange
        string value = " \t\r\n ";

        // Act & Assert
        var act = () => ValidationHelper.ValidateNotNullOrWhiteSpace(value);
        act.Should().Throw<ArgumentException>()
            .WithMessage("Value cannot be null, empty, or whitespace*");
    }

    [Fact]
    public void ValidateNotNullOrWhiteSpace_WithUnicodeWhitespace_ShouldThrow()
    {
        // Arrange - U+00A0 (non-breaking space)
        string value = "\u00A0";

        // Act & Assert
        var act = () => ValidationHelper.ValidateNotNullOrWhiteSpace(value);
        act.Should().Throw<ArgumentException>();
    }

    // ==================== ValidateMessages Collection Path ====================

    [Fact]
    public void ValidateMessages_WithArrayEmpty_ShouldThrowArgumentException()
    {
        // Arrange - Array is ICollection
        var messages = Array.Empty<TestMessage>();

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessages(messages);
        act.Should().Throw<ArgumentException>()
            .WithMessage("Messages collection cannot be empty*");
    }

    [Fact]
    public void ValidateMessages_WithListEmpty_ShouldThrowArgumentException()
    {
        // Arrange - List is ICollection
        var messages = new List<TestMessage>();

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessages(messages);
        act.Should().Throw<ArgumentException>()
            .WithMessage("Messages collection cannot be empty*");
    }

    [Fact]
    public void ValidateMessages_WithSingleElement_ShouldNotThrow()
    {
        // Arrange
        var messages = new[] { new TestMessage { MessageId = 1 } };

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessages(messages);
        act.Should().NotThrow();
    }

    // ==================== CallerArgumentExpression Tests ====================

    [Fact]
    public void ValidateNotNull_WithCustomParameterName_ShouldIncludeInException()
    {
        // Arrange
        TestMessage? myCustomVariable = null;

        // Act & Assert
        var act = () => ValidationHelper.ValidateNotNull(myCustomVariable);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("myCustomVariable");
    }

    [Fact]
    public void ValidateMessageId_WithZero_ShouldIncludeParameterName()
    {
        // Arrange
        long customIdVariable = 0;

        // Act & Assert
        var act = () => ValidationHelper.ValidateMessageId(customIdVariable);
        act.Should().Throw<ArgumentException>()
            .And.ParamName.Should().Be("customIdVariable");
    }

    [Fact]
    public void ValidateNotNullOrEmpty_WithEmpty_ShouldIncludeParameterName()
    {
        // Arrange
        string myStringValue = string.Empty;

        // Act & Assert
        var act = () => ValidationHelper.ValidateNotNullOrEmpty(myStringValue);
        act.Should().Throw<ArgumentException>()
            .And.ParamName.Should().Be("myStringValue");
    }

    // ==================== Concurrent Access Tests ====================

    [Fact]
    public void ValidationHelper_MultipleConcurrentCalls_ShouldBeThreadSafe()
    {
        // Arrange
        var messages = Enumerable.Range(1, 100)
            .Select(i => new TestMessage { MessageId = i })
            .ToArray();

        // Act - Call validation methods concurrently
        var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
        {
            ValidationHelper.ValidateMessage(messages[0]);
            ValidationHelper.ValidateMessageId(123);
            ValidationHelper.ValidateMessages(messages);
            ValidationHelper.ValidateNotNull(messages);
            ValidationHelper.ValidateNotNullOrEmpty("test");
            ValidationHelper.ValidateNotNullOrWhiteSpace("test");
        })).ToArray();

        // Assert - Should complete without exceptions
        Func<Task> act = () => Task.WhenAll(tasks);
        act.Should().NotThrowAsync();
    }

    // ==================== Test Helpers ====================

    private static IEnumerable<TestMessage> EmptyEnumerable()
    {
        // Return empty enumerable (not ICollection)
        yield break;
    }

    private static IEnumerable<TestMessage> NonEmptyEnumerable()
    {
        // Return enumerable with one item (not ICollection)
        yield return new TestMessage { MessageId = 1 };
    }

    private static IEnumerable<TestMessage> LazyEnumerable(int count)
    {
        for (int i = 1; i <= count; i++)
        {
            yield return new TestMessage { MessageId = i };
        }
    }

    public record TestMessage : IMessage
    {
        public long MessageId { get; init; }
    }

    public class NonMessage
    {
        public string Data { get; init; } = string.Empty;
    }
}

