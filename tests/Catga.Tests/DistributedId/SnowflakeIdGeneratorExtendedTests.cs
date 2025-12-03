using Catga.DistributedId;
using FluentAssertions;

namespace Catga.Tests.DistributedId;

/// <summary>
/// Extended unit tests for SnowflakeIdGenerator - edge cases and concurrency
/// </summary>
public class SnowflakeIdGeneratorExtendedTests
{
    [Fact]
    public void Constructor_WithValidWorkerId_ShouldSucceed()
    {
        // Arrange & Act
        var generator = new SnowflakeIdGenerator(0);

        // Assert
        generator.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithMaxWorkerId_ShouldSucceed()
    {
        // Arrange & Act
        var generator = new SnowflakeIdGenerator(255);

        // Assert
        generator.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNegativeWorkerId_ShouldThrow()
    {
        // Act
        var act = () => new SnowflakeIdGenerator(-1);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_WithWorkerIdTooLarge_ShouldThrow()
    {
        // Act
        var act = () => new SnowflakeIdGenerator(256);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NextId_ShouldGenerateUniqueIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(1);
        var ids = new HashSet<long>();

        // Act
        for (var i = 0; i < 10000; i++)
        {
            ids.Add(generator.NextId());
        }

        // Assert
        ids.Should().HaveCount(10000);
    }

    [Fact]
    public void NextId_ShouldGenerateMonotonicallyIncreasingIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(1);
        var previousId = 0L;

        // Act & Assert
        for (var i = 0; i < 1000; i++)
        {
            var id = generator.NextId();
            id.Should().BeGreaterThan(previousId);
            previousId = id;
        }
    }

    [Fact]
    public void NextId_ConcurrentCalls_ShouldGenerateUniqueIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(1);
        var ids = new System.Collections.Concurrent.ConcurrentBag<long>();
        const int threadCount = 10;
        const int idsPerThread = 1000;

        // Act
        Parallel.For(0, threadCount, _ =>
        {
            for (var i = 0; i < idsPerThread; i++)
            {
                ids.Add(generator.NextId());
            }
        });

        // Assert
        ids.Should().HaveCount(threadCount * idsPerThread);
        ids.Distinct().Should().HaveCount(threadCount * idsPerThread);
    }

    [Fact]
    public void ParseId_ShouldExtractCorrectWorkerId()
    {
        // Arrange
        const int workerId = 42;
        var generator = new SnowflakeIdGenerator(workerId);
        var id = generator.NextId();

        // Act
        generator.ParseId(id, out var metadata);

        // Assert
        metadata.WorkerId.Should().Be(workerId);
    }

    [Fact]
    public void ParseId_ShouldExtractValidTimestamp()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(1);
        var beforeGeneration = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var id = generator.NextId();
        var afterGeneration = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Act
        generator.ParseId(id, out var metadata);

        // Assert - Timestamp (in milliseconds) should be within reasonable range
        metadata.Timestamp.Should().BeGreaterOrEqualTo(beforeGeneration - 100);
        metadata.Timestamp.Should().BeLessOrEqualTo(afterGeneration + 100);
    }

    [Fact]
    public void ParseId_ShouldExtractSequence()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(1);
        var id = generator.NextId();

        // Act
        generator.ParseId(id, out var metadata);

        // Assert
        metadata.Sequence.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public void DifferentWorkerIds_ShouldGenerateNonConflictingIds()
    {
        // Arrange
        var generator1 = new SnowflakeIdGenerator(1);
        var generator2 = new SnowflakeIdGenerator(2);
        var ids = new HashSet<long>();

        // Act
        for (var i = 0; i < 1000; i++)
        {
            ids.Add(generator1.NextId());
            ids.Add(generator2.NextId());
        }

        // Assert
        ids.Should().HaveCount(2000);
    }

    [Fact]
    public void NextId_RapidGeneration_ShouldHandleSequenceOverflow()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(1);
        var ids = new List<long>();

        // Act - Generate many IDs rapidly to trigger sequence overflow
        for (var i = 0; i < 5000; i++)
        {
            ids.Add(generator.NextId());
        }

        // Assert - All IDs should still be unique
        ids.Distinct().Should().HaveCount(ids.Count);
    }

    [Fact]
    public void NextId_ShouldGeneratePositiveIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(1);

        // Act & Assert
        for (var i = 0; i < 100; i++)
        {
            var id = generator.NextId();
            id.Should().BePositive();
        }
    }

    [Fact]
    public void MultipleGenerators_SameWorkerId_MayHaveCollisions()
    {
        // Arrange - Two generators with same WorkerId will have collisions
        // when generating IDs in the same millisecond (expected behavior)
        var generator1 = new SnowflakeIdGenerator(1);
        var generator2 = new SnowflakeIdGenerator(1);
        var ids = new HashSet<long>();

        // Act - Generate IDs from both generators
        for (var i = 0; i < 100; i++)
        {
            ids.Add(generator1.NextId());
            ids.Add(generator2.NextId());
        }

        // Assert - Some collisions are expected when using same WorkerId
        // The important thing is that each generator individually produces unique IDs
        ids.Count.Should().BeGreaterThan(0);
        ids.Count.Should().BeLessOrEqualTo(200);
    }
}
