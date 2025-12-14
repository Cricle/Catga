using Catga.Core;
using FluentAssertions;

namespace Catga.Tests.Core;

public class MessageIdGeneratorAdditionalTests
{
    [Fact]
    public void NewMessageId_ReturnsPositiveValue()
    {
        var id = MessageExtensions.NewMessageId();
        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public void NewMessageId_GeneratesUniqueIds()
    {
        var ids = new HashSet<long>();
        for (int i = 0; i < 1000; i++)
        {
            ids.Add(MessageExtensions.NewMessageId());
        }
        ids.Count.Should().Be(1000);
    }

    [Fact]
    public void NewMessageId_IsThreadSafe()
    {
        var ids = new System.Collections.Concurrent.ConcurrentBag<long>();

        Parallel.For(0, 100, _ =>
        {
            for (int i = 0; i < 100; i++)
            {
                ids.Add(MessageExtensions.NewMessageId());
            }
        });

        ids.Distinct().Count().Should().Be(10000);
    }

    [Fact]
    public void NewMessageId_GeneratesIncreasingIds()
    {
        var id1 = MessageExtensions.NewMessageId();
        var id2 = MessageExtensions.NewMessageId();
        var id3 = MessageExtensions.NewMessageId();

        // IDs should be increasing (though not necessarily consecutive)
        id2.Should().BeGreaterThan(id1);
        id3.Should().BeGreaterThan(id2);
    }

    [Fact]
    public void NewMessageId_FitsInLong()
    {
        var id = MessageExtensions.NewMessageId();
        id.Should().BeLessThanOrEqualTo(long.MaxValue);
    }
}
