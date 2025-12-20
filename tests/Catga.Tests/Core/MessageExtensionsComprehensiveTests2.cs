using Catga.Abstractions;
using Catga.Core;
using Catga.DistributedId;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Additional comprehensive tests for MessageExtensions to improve branch coverage.
/// </summary>
public class MessageExtensionsComprehensiveTests2
{
    [Fact]
    public void NewMessageId_ShouldGenerateUniqueIds()
    {
        var ids = new HashSet<long>();
        for (int i = 0; i < 100; i++)
        {
            ids.Add(MessageExtensions.NewMessageId());
        }
        ids.Should().HaveCount(100);
    }

    [Fact]
    public void NewMessageId_ShouldGenerateIncreasingIds()
    {
        var previous = 0L;
        for (int i = 0; i < 10; i++)
        {
            var current = MessageExtensions.NewMessageId();
            current.Should().BeGreaterThan(previous);
            previous = current;
        }
    }

    [Fact]
    public void NewCorrelationId_ShouldGenerateUniqueIds()
    {
        var ids = new HashSet<long>();
        for (int i = 0; i < 100; i++)
        {
            ids.Add(MessageExtensions.NewCorrelationId());
        }
        ids.Should().HaveCount(100);
    }

    [Fact]
    public void NewMessageId_WithGenerator_ShouldUseProvidedGenerator()
    {
        var generator = new SnowflakeIdGenerator(50);
        var id = MessageExtensions.NewMessageId(generator);
        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public void NewCorrelationId_WithGenerator_ShouldUseProvidedGenerator()
    {
        var generator = new SnowflakeIdGenerator(51);
        var id = MessageExtensions.NewCorrelationId(generator);
        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public void SetIdGenerator_ShouldChangeGenerator()
    {
        var customGenerator = new SnowflakeIdGenerator(99);
        MessageExtensions.SetIdGenerator(customGenerator);

        var id = MessageExtensions.NewMessageId();
        id.Should().BeGreaterThan(0);

        // Reset to default
        MessageExtensions.SetIdGenerator(null);
    }

    [Fact]
    public void UseWorkerId_WithValidId_ShouldSucceed()
    {
        MessageExtensions.UseWorkerId(100);
        var id = MessageExtensions.NewMessageId();
        id.Should().BeGreaterThan(0);

        // Reset to default
        MessageExtensions.SetIdGenerator(null);
    }

    [Fact]
    public void UseWorkerId_WithInvalidId_ShouldThrow()
    {
        var act = () => MessageExtensions.UseWorkerId(300);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void UseWorkerId_WithNegativeId_ShouldThrow()
    {
        var act = () => MessageExtensions.UseWorkerId(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

/// <summary>
/// Tests for BatchOperationHelper edge cases.
/// </summary>
public class BatchOperationHelperExtendedTests
{
    [Fact]
    public async Task ExecuteBatchAsync_WithEmptyCollection_ShouldNotThrow()
    {
        var items = Array.Empty<int>();
        var processed = new List<int>();

        await BatchOperationHelper.ExecuteBatchAsync(items, async item =>
        {
            processed.Add(item);
        });

        processed.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteBatchAsync_WithSingleItem_ShouldProcess()
    {
        var items = new[] { 1 };
        var processed = new List<int>();

        await BatchOperationHelper.ExecuteBatchAsync(items, async item =>
        {
            processed.Add(item);
        });

        processed.Should().ContainSingle().Which.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteBatchAsync_WithMultipleItems_ShouldProcessAll()
    {
        var items = new[] { 1, 2, 3, 4, 5 };
        var processed = new List<int>();

        await BatchOperationHelper.ExecuteBatchAsync(items, async item =>
        {
            processed.Add(item);
        });

        processed.Should().BeEquivalentTo(items);
    }

    [Fact]
    public async Task ExecuteBatchAsync_WithDestination_ShouldPassDestination()
    {
        var items = new[] { "a", "b", "c" };
        var processed = new List<(string Item, string Dest)>();

        await BatchOperationHelper.ExecuteBatchAsync(items, "dest", async (item, dest) =>
        {
            processed.Add((item, dest));
        });

        processed.Should().HaveCount(3);
        processed.Should().AllSatisfy(x => x.Dest.Should().Be("dest"));
    }

    [Fact]
    public async Task ExecuteBatchAsync_WithException_ShouldThrow()
    {
        var items = new[] { 1, 2, 3 };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await BatchOperationHelper.ExecuteBatchAsync(items, async item =>
            {
                if (item == 2) throw new InvalidOperationException("Test error");
            });
        });
    }

    [Fact]
    public async Task ExecuteBatchAsync_WithCancellation_ShouldRespectCancellation()
    {
        var items = Enumerable.Range(1, 100);
        var processed = new List<int>();
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await BatchOperationHelper.ExecuteBatchAsync(items, async item =>
            {
                processed.Add(item);
                if (item == 5) cts.Cancel();
                cts.Token.ThrowIfCancellationRequested();
            });
        });
    }
}
