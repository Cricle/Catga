using Catga.DistributedId;
using FluentAssertions;

namespace Catga.Tests.Core;

/// <summary>
/// SnowflakeIdGenerator unit tests - high-performance distributed ID generation
/// </summary>
public class SnowflakeIdGeneratorTests
{
    [Fact]
    public void NextId_ShouldGenerateUniqueIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);

        // Act
        var id1 = generator.NextId();
        var id2 = generator.NextId();
        var id3 = generator.NextId();

        // Assert
        id1.Should().NotBe(id2);
        id2.Should().NotBe(id3);
        id1.Should().NotBe(id3);
    }

    [Fact]
    public void NextId_ShouldBeMonotonicallyIncreasing()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);

        // Act
        var id1 = generator.NextId();
        var id2 = generator.NextId();
        var id3 = generator.NextId();

        // Assert
        id2.Should().BeGreaterThan(id1);
        id3.Should().BeGreaterThan(id2);
    }

    [Fact]
    public void NextIds_ShouldGenerateMultipleUniqueIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);
        const int count = 1000;

        // Act
        var ids = generator.NextIds(count);

        // Assert
        ids.Length.Should().Be(count);
        ids.Distinct().Count().Should().Be(count); // All unique
    }

    [Fact]
    public void NextIds_Span_ShouldGenerateUniqueIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);
        var ids = new long[100];

        // Act
        generator.NextIds(ids.AsSpan());

        // Assert
        ids.Distinct().Count().Should().Be(100); // All unique
    }

    [Fact]
    public void TryNextId_ShouldReturnTrueAndValidId()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);

        // Act
        var success = generator.TryNextId(out var id);

        // Assert
        success.Should().BeTrue();
        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public void DifferentWorkerIds_ShouldGenerateDifferentIds()
    {
        // Arrange
        var generator1 = new SnowflakeIdGenerator(workerId: 1);
        var generator2 = new SnowflakeIdGenerator(workerId: 2);

        // Act
        var id1 = generator1.NextId();
        var id2 = generator2.NextId();

        // Assert
        id1.Should().NotBe(id2);
    }

    [Fact]
    public void HighThroughput_ShouldGenerateUniqueIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);
        const int count = 10000;
        var ids = new HashSet<long>();

        // Act
        for (int i = 0; i < count; i++)
        {
            ids.Add(generator.NextId());
        }

        // Assert
        ids.Count.Should().Be(count); // All unique
    }

    [Fact]
    public void ParseId_ShouldExtractComponents()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 5);
        var id = generator.NextId();

        // Act
        var metadata = generator.ParseId(id);

        // Assert
        metadata.WorkerId.Should().Be(5);
        metadata.Timestamp.Should().BeGreaterThan(0);
        metadata.Sequence.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public void CustomLayout_ShouldWork()
    {
        // Arrange
        var customEpoch = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var layout = SnowflakeBitLayout.WithEpoch(customEpoch);
        var generator = new SnowflakeIdGenerator(workerId: 1, layout: layout);

        // Act
        var id = generator.NextId();

        // Assert
        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ConcurrentGeneration_ShouldGenerateUniqueIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);
        const int threadsCount = 10;
        const int idsPerThread = 1000;
        var allIds = new System.Collections.Concurrent.ConcurrentBag<long>();

        // Act
        Parallel.For(0, threadsCount, _ =>
        {
            for (int i = 0; i < idsPerThread; i++)
            {
                allIds.Add(generator.NextId());
            }
        });

        // Assert
        allIds.Count.Should().Be(threadsCount * idsPerThread);
        allIds.Distinct().Count().Should().Be(threadsCount * idsPerThread); // All unique
    }

    [Fact]
    public void NextIds_WithZeroCount_ShouldThrow()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);

        // Act
        Action act = () => generator.NextIds(0);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NextIds_WithNegativeCount_ShouldThrow()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);

        // Act
        Action act = () => generator.NextIds(-1);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

