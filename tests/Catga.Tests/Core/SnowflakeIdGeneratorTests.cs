using Catga.Core;
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

    [Fact]
    public void NextIds_ShouldGenerateSequentialIds_WithinSameMillisecond()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);
        const int count = 100;
        var ids = new long[count];

        // Act
        generator.NextIds(ids.AsSpan());

        // Assert - Verify IDs are sequential (within same millisecond, sequence should increment)
        for (int i = 1; i < ids.Length; i++)
        {
            var prevSeq = ids[i - 1] & 0xFFF;  // Extract sequence (last 12 bits)
            var currSeq = ids[i] & 0xFFF;
            
            // Sequence should either increment by 1, or reset to 0 (new millisecond)
            var isSequential = (currSeq == prevSeq + 1) || (currSeq == 0 && prevSeq == 0xFFF);
            var isNewMillisecond = currSeq == 0 && ids[i] > ids[i - 1];
            
            (isSequential || isNewMillisecond).Should().BeTrue(
                $"ID at index {i} should be sequential. Previous seq: {prevSeq}, Current seq: {currSeq}");
        }
    }

    [Fact]
    public void NextIds_LargeBatch_ShouldGenerateSequentialIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);
        const int count = 10000;  // Large batch to test SIMD path
        var ids = new long[count];

        // Act
        generator.NextIds(ids.AsSpan());

        // Assert
        ids.Distinct().Count().Should().Be(count, "All IDs should be unique");
        
        // Verify first 100 IDs are sequential (likely in same millisecond)
        for (int i = 1; i < Math.Min(100, ids.Length); i++)
        {
            var prevSeq = ids[i - 1] & 0xFFF;
            var currSeq = ids[i] & 0xFFF;
            
            // Within first 100 IDs, sequence should increment (unless millisecond changed)
            if (currSeq != 0)  // If not reset to 0 (new millisecond)
            {
                currSeq.Should().Be(prevSeq + 1, 
                    $"Sequence should increment. Index: {i}, Prev: {prevSeq}, Curr: {currSeq}");
            }
        }
    }
}







