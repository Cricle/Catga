using Catga.Core;
using FluentAssertions;

namespace Catga.Tests.Core;

public class MessageExtensionsAdditionalTests
{
    [Fact]
    public void NewMessageId_GeneratesUniqueIds()
    {
        var ids = Enumerable.Range(0, 1000)
            .Select(_ => MessageExtensions.NewMessageId())
            .ToList();

        ids.Distinct().Count().Should().Be(1000);
    }

    [Fact]
    public void NewMessageId_GeneratesPositiveIds()
    {
        for (int i = 0; i < 100; i++)
        {
            var id = MessageExtensions.NewMessageId();
            id.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void NewMessageId_IsThreadSafe()
    {
        var ids = new System.Collections.Concurrent.ConcurrentBag<long>();

        Parallel.For(0, 1000, _ =>
        {
            ids.Add(MessageExtensions.NewMessageId());
        });

        ids.Distinct().Count().Should().Be(1000);
    }

    [Fact]
    public void NewMessageId_GeneratesSequentiallyIncreasing()
    {
        var id1 = MessageExtensions.NewMessageId();
        var id2 = MessageExtensions.NewMessageId();
        var id3 = MessageExtensions.NewMessageId();

        id2.Should().BeGreaterThan(id1);
        id3.Should().BeGreaterThan(id2);
    }
}
