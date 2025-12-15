using Catga.Core;
using FluentAssertions;

namespace Catga.Tests.Core;

/// <summary>
/// Basic tests for MessageExtensions utility methods
/// </summary>
public class MessageExtensionsBasicTests
{
    #region NewMessageId Tests

    [Fact]
    public void NewMessageId_ReturnsLong()
    {
        var id = MessageExtensions.NewMessageId();

        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public void NewMessageId_ReturnsPositive()
    {
        var id = MessageExtensions.NewMessageId();

        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public void NewMessageId_MultipleCallsReturnDifferentValues()
    {
        var id1 = MessageExtensions.NewMessageId();
        var id2 = MessageExtensions.NewMessageId();

        id1.Should().NotBe(id2);
    }

    #endregion

    #region Consistency Tests

    [Fact]
    public void NewMessageId_ConsistentBehavior()
    {
        var ids = Enumerable.Range(0, 10)
            .Select(_ => MessageExtensions.NewMessageId())
            .ToList();

        ids.All(id => id > 0).Should().BeTrue();
        ids.Distinct().Count().Should().Be(10);
    }

    #endregion
}
