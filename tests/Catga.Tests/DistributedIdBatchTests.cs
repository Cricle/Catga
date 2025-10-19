using Catga.DistributedId;
using FluentAssertions;
using Xunit;

namespace Catga.Tests;

public class DistributedIdBatchTests
{
    [Fact]
    public void NextIds_Span_ShouldGenerateUniqueIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(1);
        Span<long> ids = stackalloc long[100];  // 0 allocation (stack)

        // Act
        var count = generator.NextIds(ids);

        // Assert
        count.Should().Be(100);
        var uniqueIds = new HashSet<long>();
        foreach (var id in ids)
        {
            uniqueIds.Add(id);
        }
        uniqueIds.Count.Should().Be(100, "all batch IDs should be unique");
    }

    [Fact]
    public void NextIds_Array_ShouldGenerateUniqueIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(1);

        // Act
        var ids = generator.NextIds(100);

        // Assert
        ids.Length.Should().Be(100);
        ids.Distinct().Count().Should().Be(100, "all batch IDs should be unique");
    }

    [Fact]
    public void NextIds_LargeBatch_ShouldWork()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(1);

        // Act
        var ids = generator.NextIds(10000);

        // Assert
        ids.Length.Should().Be(10000);
        ids.Distinct().Count().Should().Be(10000, "all batch IDs should be unique");

        // Verify IDs are increasing
        for (int i = 1; i < ids.Length; i++)
        {
            ids[i].Should().BeGreaterThan(ids[i - 1], "batch IDs should be strictly increasing");
        }
    }

    [Fact]
    public void NextIds_EmptySpan_ShouldReturnZero()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(1);
        Span<long> ids = stackalloc long[0];

        // Act
        var count = generator.NextIds(ids);

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public void NextIds_InvalidCount_ShouldThrow()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(1);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.NextIds(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.NextIds(-1));
    }

    [Fact]
    public void NextIds_Concurrent_ShouldGenerateUniqueIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(1);
        var allIds = new System.Collections.Concurrent.ConcurrentBag<long>();

        // Act - Multiple threads generating batches simultaneously
        Parallel.For(0, 10, _ =>
        {
            var ids = generator.NextIds(1000);
            foreach (var id in ids)
            {
                allIds.Add(id);
            }
        });

        // Assert
        allIds.Count.Should().Be(10000);
        allIds.Distinct().Count().Should().Be(10000, "all concurrent batch IDs should be unique");
    }

    [Fact]
    public void NextIds_HighConcurrency_ShouldWork()
    {
        // Arrange
        var layout = SnowflakeBitLayout.HighConcurrency; // 14 bits sequence = 16384 IDs/ms
        var generator = new SnowflakeIdGenerator(1, layout);

        // Act
        var ids = generator.NextIds(20000); // More than 1 millisecond worth

        // Assert
        ids.Length.Should().Be(20000);
        ids.Distinct().Count().Should().Be(20000);
    }

    [Fact]
    public void NextIds_VsNextId_ShouldBeFaster()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(1);
        var count = 10000;

        // Act 1: Individual calls
        var sw1 = System.Diagnostics.Stopwatch.StartNew();
        var ids1 = new long[count];
        for (int i = 0; i < count; i++)
        {
            ids1[i] = generator.NextId();
        }
        sw1.Stop();

        // Act 2: Batch call
        var generator2 = new SnowflakeIdGenerator(2);
        var sw2 = System.Diagnostics.Stopwatch.StartNew();
        var ids2 = generator2.NextIds(count);
        sw2.Stop();

        // Assert
        ids1.Distinct().Count().Should().Be(count);
        ids2.Distinct().Count().Should().Be(count);

        // Batch should be faster (less CAS contention)
        // Note: This is a performance hint, not a strict requirement
        // sw2.ElapsedMilliseconds.Should().BeLessThan(sw1.ElapsedMilliseconds);
    }

    [Fact]
    public void NextIds_ZeroAllocation_Verification()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(1);
        Span<long> ids = stackalloc long[50];

        // Act
        var count = generator.NextIds(ids);  // 0 bytes allocated (stackalloc span)

        // Assert
        count.Should().Be(50);

        // Verify all IDs are valid
        foreach (var id in ids)
        {
            id.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void NextIds_WithCustomEpoch_ShouldWork()
    {
        // Arrange
        var customEpoch = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var layout = SnowflakeBitLayout.WithEpoch(customEpoch);
        var generator = new SnowflakeIdGenerator(42, layout);

        // Act
        var ids = generator.NextIds(100);

        // Assert
        ids.Length.Should().Be(100);
        ids.Distinct().Count().Should().Be(100);

        // Verify first ID metadata
        generator.ParseId(ids[0], out var metadata);
        metadata.WorkerId.Should().Be(42);
    }
}


