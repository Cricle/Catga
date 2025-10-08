using Catga.DistributedId;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests;

public class DistributedIdTests
{
    [Fact]
    public void NextId_ShouldGenerateUniqueIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(1);
        var ids = new HashSet<long>();

        // Act
        for (int i = 0; i < 10000; i++)
        {
            ids.Add(generator.NextId());
        }

        // Assert
        ids.Count.Should().Be(10000, "all IDs should be unique");
    }

    [Fact]
    public void NextId_ShouldGenerateIncreasingIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(1);
        var previousId = 0L;

        // Act & Assert
        for (int i = 0; i < 1000; i++)
        {
            var id = generator.NextId();
            id.Should().BeGreaterThan(previousId, "IDs should be increasing");
            previousId = id;
        }
    }

    [Fact]
    public void NextId_WithDifferentWorkers_ShouldGenerateDifferentIds()
    {
        // Arrange
        var generator1 = new SnowflakeIdGenerator(1);
        var generator2 = new SnowflakeIdGenerator(2);

        // Act
        var id1 = generator1.NextId();
        var id2 = generator2.NextId();

        // Assert
        id1.Should().NotBe(id2, "different workers should generate different IDs");
    }

    [Fact]
    public void ParseId_ShouldExtractCorrectMetadata()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(42);
        var id = generator.NextId();

        // Act
        var metadata = generator.ParseId(id);

        // Assert
        metadata.WorkerId.Should().Be(42);
        metadata.Sequence.Should().BeGreaterThanOrEqualTo(0);
        metadata.GeneratedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ParseId_ZeroAllocation_ShouldWork()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(42);
        var id = generator.NextId();

        // Act
        generator.ParseId(id, out var metadata);

        // Assert
        metadata.WorkerId.Should().Be(42);
        metadata.Sequence.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void CustomLayout_LongLifespan_ShouldWork()
    {
        // Arrange
        var layout = SnowflakeBitLayout.LongLifespan;
        var generator = new SnowflakeIdGenerator(10, layout);

        // Act
        var id = generator.NextId();
        var metadata = generator.ParseId(id);

        // Assert
        metadata.WorkerId.Should().Be(10);
        layout.MaxYears.Should().BeGreaterThan(200);
    }

    [Fact]
    public void CustomLayout_HighConcurrency_ShouldWork()
    {
        // Arrange
        var layout = SnowflakeBitLayout.HighConcurrency;
        var generator = new SnowflakeIdGenerator(10, layout);

        // Act
        var ids = new HashSet<long>();
        for (int i = 0; i < 20000; i++)
        {
            ids.Add(generator.NextId());
        }

        // Assert
        ids.Count.Should().Be(20000, "all IDs should be unique in high concurrency mode");
    }

    [Fact]
    public void TryWriteNextId_ShouldWork()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(1);
        Span<char> buffer = stackalloc char[20];

        // Act
        var success = generator.TryWriteNextId(buffer, out var charsWritten);

        // Assert
        success.Should().BeTrue();
        charsWritten.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Constructor_WithInvalidWorkerId_ShouldThrow()
    {
        // Act & Assert
        Action act1 = () => new SnowflakeIdGenerator(-1);
        Action act2 = () => new SnowflakeIdGenerator(1024);

        act1.Should().Throw<ArgumentOutOfRangeException>();
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NextIdString_ShouldReturnStringId()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(1);

        // Act
        var idString = generator.NextIdString();

        // Assert
        idString.Should().NotBeNullOrEmpty();
        long.TryParse(idString, out _).Should().BeTrue("ID string should be parseable as long");
    }

    [Fact]
    public void AddDistributedId_ShouldRegisterGenerator()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddDistributedId(options =>
        {
            options.WorkerId = 10;
            options.AutoDetectWorkerId = false;
        });

        var provider = services.BuildServiceProvider();
        var generator = provider.GetRequiredService<IDistributedIdGenerator>();

        // Assert
        generator.Should().NotBeNull();
        var id = generator.NextId();
        var metadata = generator.ParseId(id);
        metadata.WorkerId.Should().Be(10);
    }

    [Fact]
    public void AddDistributedId_WithCustomLayout_ShouldWork()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddDistributedId(options =>
        {
            options.WorkerId = 5;
            options.AutoDetectWorkerId = false;
            options.Layout = SnowflakeBitLayout.UltraLongLifespan;
        });

        var provider = services.BuildServiceProvider();
        var generator = provider.GetRequiredService<IDistributedIdGenerator>() as SnowflakeIdGenerator;

        // Assert
        generator.Should().NotBeNull();
        var layout = generator!.GetLayout();
        layout.MaxYears.Should().BeGreaterThan(1000);
    }

    [Fact]
    public void AddDistributedId_WithExplicitWorkerId_ShouldWork()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddDistributedId(workerId: 99);

        var provider = services.BuildServiceProvider();
        var generator = provider.GetRequiredService<IDistributedIdGenerator>();

        // Assert
        var id = generator.NextId();
        var metadata = generator.ParseId(id);
        metadata.WorkerId.Should().Be(99);
    }

    [Fact]
    public void DistributedIdOptions_Validate_ShouldThrowForInvalidWorkerId()
    {
        // Arrange
        var options = new DistributedIdOptions { WorkerId = -1 };

        // Act & Assert
        Action act = () => options.Validate();
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NextId_UnderLoad_ShouldGenerateUniqueIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(1);
        var ids = new System.Collections.Concurrent.ConcurrentBag<long>();

        // Act - Parallel generation
        Parallel.For(0, 10000, _ =>
        {
            ids.Add(generator.NextId());
        });

        // Assert
        var uniqueIds = new HashSet<long>(ids);
        uniqueIds.Count.Should().Be(10000, "all IDs should be unique even under concurrent load");
    }
}

