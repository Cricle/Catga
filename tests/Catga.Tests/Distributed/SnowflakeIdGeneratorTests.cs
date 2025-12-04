using Catga.DistributedId;
using FluentAssertions;
using System.Collections.Concurrent;
using Xunit;

namespace Catga.Tests.Distributed;

/// <summary>
/// Tests for Snowflake ID Generator
/// Coverage: Basic generation, uniqueness, monotonicity, concurrency, edge cases
/// </summary>
public class SnowflakeIdGeneratorTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithDefaultLayout_ShouldInitialize()
    {
        // Arrange & Act
        var generator = new SnowflakeIdGenerator(workerId: 1);

        // Assert
        var layout = generator.GetLayout();
        layout.TimestampBits.Should().Be(44);
        layout.WorkerIdBits.Should().Be(8);
        layout.SequenceBits.Should().Be(11);
    }

    [Fact]
    public void Constructor_WithCustomLayout_ShouldInitialize()
    {
        // Arrange
        var customLayout = new SnowflakeBitLayout
        {
            TimestampBits = 40,
            WorkerIdBits = 10,
            SequenceBits = 13,
            EpochMilliseconds = 1704067200000L
        };

        // Act
        var generator = new SnowflakeIdGenerator(workerId: 1, layout: customLayout);

        // Assert
        var layout = generator.GetLayout();
        layout.TimestampBits.Should().Be(40);
        layout.WorkerIdBits.Should().Be(10);
        layout.SequenceBits.Should().Be(13);
    }

    [Fact]
    public void Constructor_WithNegativeWorkerId_ShouldThrow()
    {
        // Arrange, Act & Assert
        var act = () => new SnowflakeIdGenerator(workerId: -1);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("workerId");
    }

    [Fact]
    public void Constructor_WithWorkerIdExceedingMax_ShouldThrow()
    {
        // Arrange
        var layout = SnowflakeBitLayout.Default; // MaxWorkerId = 255

        // Act & Assert
        var act = () => new SnowflakeIdGenerator(workerId: 256, layout: layout);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("workerId");
    }

    #endregion

    #region NextId Tests

    [Fact]
    public void NextId_ShouldGeneratePositiveId()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);

        // Act
        var id = generator.NextId();

        // Assert
        id.Should().BePositive();
    }

    [Fact]
    public void NextId_MultipleCalls_ShouldGenerateUniqueIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);
        var ids = new HashSet<long>();

        // Act
        for (int i = 0; i < 10000; i++)
        {
            var id = generator.NextId();
            ids.Add(id).Should().BeTrue($"ID {id} should be unique");
        }

        // Assert
        ids.Count.Should().Be(10000);
    }

    [Fact]
    public void NextId_MultipleCalls_ShouldBeMonotonicallyIncreasing()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);
        var previousId = 0L;

        // Act & Assert
        for (int i = 0; i < 1000; i++)
        {
            var id = generator.NextId();
            id.Should().BeGreaterThan(previousId);
            previousId = id;
        }
    }

    [Fact]
    public void NextId_ShouldEmbedCorrectWorkerId()
    {
        // Arrange
        const int workerId = 42;
        var generator = new SnowflakeIdGenerator(workerId: workerId);

        // Act
        var id = generator.NextId();
        var metadata = generator.ParseId(id);

        // Assert
        metadata.WorkerId.Should().Be(workerId);
    }

    #endregion

    #region NextIdString Tests

    [Fact]
    public void NextIdString_ShouldReturnValidString()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);

        // Act
        var idString = generator.NextIdString();

        // Assert
        idString.Should().NotBeNullOrEmpty();
        long.TryParse(idString, out var parsed).Should().BeTrue();
        parsed.Should().BePositive();
    }

    #endregion

    #region TryNextId Tests

    [Fact]
    public void TryNextId_ShouldReturnTrueAndValidId()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);

        // Act
        var success = generator.TryNextId(out var id);

        // Assert
        success.Should().BeTrue();
        id.Should().BePositive();
    }

    [Fact]
    public void TryNextId_MultipleCalls_ShouldGenerateUniqueIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);
        var ids = new HashSet<long>();

        // Act
        for (int i = 0; i < 1000; i++)
        {
            generator.TryNextId(out var id).Should().BeTrue();
            ids.Add(id).Should().BeTrue();
        }

        // Assert
        ids.Count.Should().Be(1000);
    }

    #endregion

    #region NextIds (Span) Tests

    [Fact]
    public void NextIds_Span_WithEmptySpan_ShouldReturnZero()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);
        Span<long> destination = Span<long>.Empty;

        // Act
        var generated = generator.NextIds(destination);

        // Assert
        generated.Should().Be(0);
    }

    [Fact]
    public void NextIds_Span_ShouldGenerateRequestedCount()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);
        Span<long> destination = stackalloc long[100];

        // Act
        var generated = generator.NextIds(destination);

        // Assert
        generated.Should().Be(100);
        foreach (var id in destination)
        {
            id.Should().BePositive();
        }
    }

    [Fact]
    public void NextIds_Span_ShouldGenerateUniqueIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);
        Span<long> destination = stackalloc long[1000];

        // Act
        generator.NextIds(destination);

        // Assert
        var uniqueIds = new HashSet<long>();
        foreach (var id in destination)
        {
            uniqueIds.Add(id).Should().BeTrue($"ID {id} should be unique");
        }
        uniqueIds.Count.Should().Be(1000);
    }

    [Fact]
    public void NextIds_Span_ShouldBeMonotonicallyIncreasing()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);
        Span<long> destination = stackalloc long[500];

        // Act
        generator.NextIds(destination);

        // Assert
        for (int i = 1; i < destination.Length; i++)
        {
            destination[i].Should().BeGreaterThan(destination[i - 1]);
        }
    }

    [Fact]
    public void NextIds_Span_LargeBatch_ShouldGenerateAllIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);
        var destination = new long[15000]; // > 10k triggers adaptive batch

        // Act
        var generated = generator.NextIds(destination.AsSpan());

        // Assert
        generated.Should().Be(15000);
        var uniqueIds = new HashSet<long>(destination);
        uniqueIds.Count.Should().Be(15000);
    }

    #endregion

    #region NextIds (Array) Tests

    [Fact]
    public void NextIds_Array_WithZeroCount_ShouldThrow()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);

        // Act & Assert
        var act = () => generator.NextIds(count: 0);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("count");
    }

    [Fact]
    public void NextIds_Array_WithNegativeCount_ShouldThrow()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);

        // Act & Assert
        var act = () => generator.NextIds(count: -1);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("count");
    }

    [Fact]
    public void NextIds_Array_ShouldReturnArrayWithRequestedCount()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);

        // Act
        var ids = generator.NextIds(count: 100);

        // Assert
        ids.Should().HaveCount(100);
        ids.Should().OnlyContain(id => id > 0);
    }

    [Fact]
    public void NextIds_Array_ShouldGenerateUniqueIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);

        // Act
        var ids = generator.NextIds(count: 1000);

        // Assert
        ids.Distinct().Should().HaveCount(1000);
    }

    #endregion

    #region TryWriteNextId Tests

    [Fact]
    public void TryWriteNextId_WithSufficientBuffer_ShouldReturnTrue()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);
        Span<char> buffer = stackalloc char[30];

        // Act
        var success = generator.TryWriteNextId(buffer, out var charsWritten);

        // Assert
        success.Should().BeTrue();
        charsWritten.Should().BeGreaterThan(0);
        var idString = new string(buffer.Slice(0, charsWritten));
        long.TryParse(idString, out _).Should().BeTrue();
    }

    [Fact]
    public void TryWriteNextId_WithInsufficientBuffer_ShouldReturnFalse()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);
        Span<char> buffer = stackalloc char[2]; // Too small

        // Act
        var success = generator.TryWriteNextId(buffer, out var charsWritten);

        // Assert
        success.Should().BeFalse();
    }

    #endregion

    #region ParseId Tests

    [Fact]
    public void ParseId_ShouldExtractCorrectMetadata()
    {
        // Arrange
        const int workerId = 123;
        var generator = new SnowflakeIdGenerator(workerId: workerId);
        var id = generator.NextId();

        // Act
        var metadata = generator.ParseId(id);

        // Assert
        metadata.WorkerId.Should().Be(workerId);
        metadata.Timestamp.Should().BeGreaterThan(0);
        metadata.Sequence.Should().BeGreaterOrEqualTo(0);
        metadata.GeneratedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ParseId_OutVersion_ShouldExtractCorrectMetadata()
    {
        // Arrange
        const int workerId = 99;
        var generator = new SnowflakeIdGenerator(workerId: workerId);
        var id = generator.NextId();

        // Act
        generator.ParseId(id, out var metadata);

        // Assert
        metadata.WorkerId.Should().Be(workerId);
        metadata.Timestamp.Should().BeGreaterThan(0);
        metadata.Sequence.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public void ParseId_RoundTrip_ShouldPreserveWorkerId()
    {
        // Arrange
        var workerIds = new[] { 0, 1, 42, 127, 255 };

        foreach (var workerId in workerIds)
        {
            var generator = new SnowflakeIdGenerator(workerId: workerId);
            var id = generator.NextId();

            // Act
            var metadata = generator.ParseId(id);

            // Assert
            metadata.WorkerId.Should().Be(workerId, $"WorkerId {workerId} should be preserved");
        }
    }

    [Fact]
    public void ParseId_SequentialIds_ShouldShowIncrementingSequence()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);

        // Act
        var id1 = generator.NextId();
        var id2 = generator.NextId();
        var metadata1 = generator.ParseId(id1);
        var metadata2 = generator.ParseId(id2);

        // Assert
        // If same timestamp, sequence should increment
        if (metadata1.Timestamp == metadata2.Timestamp)
        {
            metadata2.Sequence.Should().BeGreaterThan(metadata1.Sequence);
        }
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task NextId_ConcurrentGeneration_ShouldGenerateUniqueIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);
        var ids = new ConcurrentBag<long>();
        var tasks = new List<Task>();
        const int tasksCount = 10;
        const int idsPerTask = 1000;

        // Act
        for (int i = 0; i < tasksCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < idsPerTask; j++)
                {
                    var id = generator.NextId();
                    ids.Add(id);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        ids.Count.Should().Be(tasksCount * idsPerTask);
        ids.Distinct().Count().Should().Be(tasksCount * idsPerTask, "All IDs should be unique");
    }

    [Fact]
    public async Task NextIds_ConcurrentBatchGeneration_ShouldGenerateUniqueIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);
        var allIds = new ConcurrentBag<long>();
        var tasks = new List<Task>();
        const int tasksCount = 10;
        const int batchSize = 500;

        // Act
        for (int i = 0; i < tasksCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                var ids = generator.NextIds(count: batchSize);
                foreach (var id in ids)
                {
                    allIds.Add(id);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        allIds.Count.Should().Be(tasksCount * batchSize);
        allIds.Distinct().Count().Should().Be(tasksCount * batchSize, "All IDs should be unique");
    }

    #endregion

    #region Custom Layout Tests

    [Fact]
    public void NextId_WithHighConcurrencyLayout_ShouldWork()
    {
        // Arrange
        var layout = SnowflakeBitLayout.HighConcurrency;
        var generator = new SnowflakeIdGenerator(workerId: 500, layout: layout);

        // Act
        var ids = generator.NextIds(count: 100);

        // Assert
        ids.Should().OnlyContain(id => id > 0);
        ids.Distinct().Should().HaveCount(100);
    }

    [Fact]
    public void NextId_WithLargeClusterLayout_ShouldWork()
    {
        // Arrange
        var layout = SnowflakeBitLayout.LargeCluster;
        var generator = new SnowflakeIdGenerator(workerId: 2000, layout: layout);

        // Act
        var ids = generator.NextIds(count: 100);

        // Assert
        ids.Should().OnlyContain(id => id > 0);
        ids.Distinct().Should().HaveCount(100);
    }

    [Fact]
    public void NextId_WithUltraLongLifespanLayout_ShouldWork()
    {
        // Arrange
        var layout = SnowflakeBitLayout.UltraLongLifespan;
        var generator = new SnowflakeIdGenerator(workerId: 32, layout: layout);

        // Act
        var ids = generator.NextIds(count: 100);

        // Assert
        ids.Should().OnlyContain(id => id > 0);
        ids.Distinct().Should().HaveCount(100);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void NextId_WithMaxWorkerId_ShouldWork()
    {
        // Arrange
        var layout = SnowflakeBitLayout.Default; // MaxWorkerId = 255
        var generator = new SnowflakeIdGenerator(workerId: 255, layout: layout);

        // Act
        var id = generator.NextId();
        var metadata = generator.ParseId(id);

        // Assert
        id.Should().BePositive();
        metadata.WorkerId.Should().Be(255);
    }

    [Fact]
    public void NextId_WithZeroWorkerId_ShouldWork()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 0);

        // Act
        var id = generator.NextId();
        var metadata = generator.ParseId(id);

        // Assert
        id.Should().BePositive();
        metadata.WorkerId.Should().Be(0);
    }

    [Fact]
    public void NextIds_ExceedingSequenceLimit_ShouldWaitForNextMillisecond()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(workerId: 1);
        var layout = generator.GetLayout();
        var maxSequence = (int)layout.MaxSequence;

        // Act - Generate more IDs than sequence allows in a single millisecond
        var ids = generator.NextIds(count: maxSequence + 100);

        // Assert
        ids.Should().HaveCount(maxSequence + 100);
        ids.Distinct().Should().HaveCount(maxSequence + 100, "All IDs should be unique even when exceeding sequence limit");
    }

    #endregion

    #region GetLayout Tests

    [Fact]
    public void GetLayout_ShouldReturnConfiguredLayout()
    {
        // Arrange
        var customLayout = new SnowflakeBitLayout
        {
            TimestampBits = 42,
            WorkerIdBits = 9,
            SequenceBits = 12,
            EpochMilliseconds = 1704067200000L
        };
        var generator = new SnowflakeIdGenerator(workerId: 1, layout: customLayout);

        // Act
        var layout = generator.GetLayout();

        // Assert
        layout.TimestampBits.Should().Be(42);
        layout.WorkerIdBits.Should().Be(9);
        layout.SequenceBits.Should().Be(12);
        layout.EpochMilliseconds.Should().Be(1704067200000L);
    }

    #endregion
}

