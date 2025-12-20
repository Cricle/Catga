using Catga.DistributedId;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Extended tests for SnowflakeIdGenerator to improve branch coverage.
/// </summary>
public class SnowflakeIdGeneratorExtendedTests
{
    [Fact]
    public void Constructor_WithDefaultLayout_ShouldSucceed()
    {
        var generator = new SnowflakeIdGenerator(0);
        generator.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithCustomWorkerId_ShouldSucceed()
    {
        var generator = new SnowflakeIdGenerator(100);
        generator.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithInvalidWorkerId_ShouldThrow()
    {
        var act = () => new SnowflakeIdGenerator(2000); // Too large
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_WithNegativeWorkerId_ShouldThrow()
    {
        var act = () => new SnowflakeIdGenerator(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_WithCustomLayout_ShouldSucceed()
    {
        var layout = SnowflakeBitLayout.Default;
        var generator = new SnowflakeIdGenerator(0, layout);
        generator.Should().NotBeNull();
    }

    [Fact]
    public void NextId_ShouldGenerateUniqueIds()
    {
        var generator = new SnowflakeIdGenerator(0);
        var ids = new HashSet<long>();

        for (int i = 0; i < 1000; i++)
        {
            ids.Add(generator.NextId());
        }

        ids.Should().HaveCount(1000);
    }

    [Fact]
    public void NextId_ShouldGenerateIncreasingIds()
    {
        var generator = new SnowflakeIdGenerator(0);
        var previous = 0L;

        for (int i = 0; i < 100; i++)
        {
            var current = generator.NextId();
            current.Should().BeGreaterThan(previous);
            previous = current;
        }
    }

    [Fact]
    public void NextId_ConcurrentCalls_ShouldGenerateUniqueIds()
    {
        var generator = new SnowflakeIdGenerator(0);
        var ids = new System.Collections.Concurrent.ConcurrentBag<long>();

        Parallel.For(0, 1000, _ =>
        {
            ids.Add(generator.NextId());
        });

        ids.Distinct().Should().HaveCount(1000);
    }

    [Fact]
    public void TryNextId_ShouldSucceed()
    {
        var generator = new SnowflakeIdGenerator(0);
        var result = generator.TryNextId(out var id);

        result.Should().BeTrue();
        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public void NextIdString_ShouldReturnStringId()
    {
        var generator = new SnowflakeIdGenerator(0);
        var idString = generator.NextIdString();

        idString.Should().NotBeNullOrEmpty();
        long.TryParse(idString, out _).Should().BeTrue();
    }

    [Fact]
    public void ParseId_ShouldExtractMetadata()
    {
        var generator = new SnowflakeIdGenerator(42);
        var id = generator.NextId();

        var metadata = generator.ParseId(id);

        metadata.WorkerId.Should().Be(42);
        metadata.GeneratedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ParseId_OutParameter_ShouldExtractMetadata()
    {
        var generator = new SnowflakeIdGenerator(42);
        var id = generator.NextId();

        generator.ParseId(id, out var metadata);

        metadata.WorkerId.Should().Be(42);
        metadata.Sequence.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void NextIds_Span_ShouldGenerateMultipleIds()
    {
        var generator = new SnowflakeIdGenerator(0);
        var ids = new long[100];

        var count = generator.NextIds(ids.AsSpan());

        count.Should().Be(100);
        ids.Distinct().Should().HaveCount(100);
    }

    [Fact]
    public void NextIds_Array_ShouldGenerateMultipleIds()
    {
        var generator = new SnowflakeIdGenerator(0);
        var ids = generator.NextIds(100);

        ids.Should().HaveCount(100);
        ids.Distinct().Should().HaveCount(100);
    }

    [Fact]
    public void NextIds_EmptySpan_ShouldReturnZero()
    {
        var generator = new SnowflakeIdGenerator(0);
        var ids = Array.Empty<long>();

        var count = generator.NextIds(ids.AsSpan());

        count.Should().Be(0);
    }

    [Fact]
    public void NextIds_InvalidCount_ShouldThrow()
    {
        var generator = new SnowflakeIdGenerator(0);
        var act = () => generator.NextIds(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NextIds_LargeBatch_ShouldSucceed()
    {
        var generator = new SnowflakeIdGenerator(0);
        var ids = generator.NextIds(10000);

        ids.Should().HaveCount(10000);
        ids.Distinct().Should().HaveCount(10000);
    }

    [Fact]
    public void TryWriteNextId_ShouldWriteToSpan()
    {
        var generator = new SnowflakeIdGenerator(0);
        var buffer = new char[30];

        var result = generator.TryWriteNextId(buffer.AsSpan(), out var charsWritten);

        result.Should().BeTrue();
        charsWritten.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetLayout_ShouldReturnLayout()
    {
        var generator = new SnowflakeIdGenerator(0);
        var layout = generator.GetLayout();

        layout.Should().NotBeNull();
        layout.SequenceBits.Should().BeGreaterThan(0);
    }

    [Fact]
    public void SnowflakeBitLayout_Default_ShouldHaveCorrectValues()
    {
        var layout = SnowflakeBitLayout.Default;

        layout.TimestampBits.Should().BeGreaterThan(0);
        layout.WorkerIdBits.Should().BeGreaterThan(0);
        layout.SequenceBits.Should().BeGreaterThan(0);
    }

    [Fact]
    public void SnowflakeBitLayout_MaxWorkerId_ShouldBeCalculatedCorrectly()
    {
        var layout = SnowflakeBitLayout.Default;
        layout.MaxWorkerId.Should().BeGreaterThan(0);
    }

    [Fact]
    public void SnowflakeBitLayout_SequenceMask_ShouldBeCalculatedCorrectly()
    {
        var layout = SnowflakeBitLayout.Default;
        layout.SequenceMask.Should().BeGreaterThan(0);
    }

    [Fact]
    public void DifferentWorkers_ShouldGenerateDifferentIds()
    {
        var gen1 = new SnowflakeIdGenerator(1);
        var gen2 = new SnowflakeIdGenerator(2);

        var id1 = gen1.NextId();
        var id2 = gen2.NextId();

        id1.Should().NotBe(id2);
    }

    [Fact]
    public void ParseId_DifferentWorkers_ShouldShowDifferentWorkerIds()
    {
        var gen1 = new SnowflakeIdGenerator(10);
        var gen2 = new SnowflakeIdGenerator(20);

        var id1 = gen1.NextId();
        var id2 = gen2.NextId();

        gen1.ParseId(id1).WorkerId.Should().Be(10);
        gen2.ParseId(id2).WorkerId.Should().Be(20);
    }

    [Fact]
    public void IdMetadata_ShouldContainAllFields()
    {
        var generator = new SnowflakeIdGenerator(5);
        var id = generator.NextId();
        var metadata = generator.ParseId(id);

        metadata.Timestamp.Should().BeGreaterThan(0);
        metadata.WorkerId.Should().Be(5);
        metadata.Sequence.Should().BeGreaterThanOrEqualTo(0);
        metadata.GeneratedAt.Should().NotBe(default);
    }
}
