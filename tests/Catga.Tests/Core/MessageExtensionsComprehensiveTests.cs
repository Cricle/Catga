using Catga.Core;
using FluentAssertions;

namespace Catga.Tests.Core;

/// <summary>
/// Comprehensive tests for MessageExtensions
/// </summary>
public class MessageExtensionsComprehensiveTests
{
    #region MessageId Generation Tests

    [Fact]
    public void NewMessageId_GeneratesPositiveValue()
    {
        var id = MessageExtensions.NewMessageId();

        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public void NewMessageId_GeneratesUniqueValues()
    {
        var ids = new HashSet<long>();

        for (int i = 0; i < 100; i++)
        {
            ids.Add(MessageExtensions.NewMessageId());
        }

        ids.Count.Should().Be(100);
    }

    [Fact]
    public void NewMessageId_IsIncreasing()
    {
        var id1 = MessageExtensions.NewMessageId();
        var id2 = MessageExtensions.NewMessageId();

        id2.Should().BeGreaterThan(id1);
    }

    #endregion

    #region Concurrent MessageId Tests

    [Fact]
    public void NewMessageId_ConcurrentGeneration_AllUnique()
    {
        var ids = new System.Collections.Concurrent.ConcurrentBag<long>();

        Parallel.For(0, 1000, _ =>
        {
            ids.Add(MessageExtensions.NewMessageId());
        });

        ids.Distinct().Count().Should().Be(1000);
    }

    #endregion

    #region MessageId Consistency Tests

    [Fact]
    public void NewMessageId_ConsistentBehavior()
    {
        var id1 = MessageExtensions.NewMessageId();
        var id2 = MessageExtensions.NewMessageId();
        var id3 = MessageExtensions.NewMessageId();

        id1.Should().BeLessThan(id2);
        id2.Should().BeLessThan(id3);
    }

    #endregion
}
