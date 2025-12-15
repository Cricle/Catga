using Catga.Core;
using FluentAssertions;

namespace Catga.Tests.Core;

/// <summary>
/// Tests for MessageId uniqueness and generation
/// </summary>
public class MessageIdUniquenessTests
{
    #region Uniqueness Tests

    [Fact]
    public void MessageId_SequentialGeneration_AllUnique()
    {
        var ids = new HashSet<long>();

        for (int i = 0; i < 1000; i++)
        {
            ids.Add(MessageExtensions.NewMessageId());
        }

        ids.Count.Should().Be(1000);
    }

    [Fact]
    public void MessageId_ConcurrentGeneration_AllUnique()
    {
        var ids = new System.Collections.Concurrent.ConcurrentBag<long>();

        Parallel.For(0, 1000, _ =>
        {
            ids.Add(MessageExtensions.NewMessageId());
        });

        ids.Distinct().Count().Should().Be(1000);
    }

    [Fact]
    public void MessageId_HighConcurrency_AllUnique()
    {
        var ids = new System.Collections.Concurrent.ConcurrentBag<long>();

        Parallel.For(0, 10000, _ =>
        {
            ids.Add(MessageExtensions.NewMessageId());
        });

        ids.Distinct().Count().Should().Be(10000);
    }

    #endregion

    #region MessageId Properties Tests

    [Fact]
    public void MessageId_IsPositive()
    {
        var id = MessageExtensions.NewMessageId();

        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public void MessageId_AllPositive()
    {
        var ids = Enumerable.Range(0, 100)
            .Select(_ => MessageExtensions.NewMessageId())
            .ToList();

        ids.All(id => id > 0).Should().BeTrue();
    }

    #endregion

    #region Monotonic Increase Tests

    [Fact]
    public void MessageId_GenerallyIncreasing()
    {
        var id1 = MessageExtensions.NewMessageId();
        var id2 = MessageExtensions.NewMessageId();
        var id3 = MessageExtensions.NewMessageId();

        (id1 < id2).Should().BeTrue();
        (id2 < id3).Should().BeTrue();
    }

    #endregion
}
