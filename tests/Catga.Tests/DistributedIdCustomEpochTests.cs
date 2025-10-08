using Catga.DistributedId;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests;

public class DistributedIdCustomEpochTests
{
    [Fact]
    public void CustomEpoch_ShouldWork()
    {
        // Arrange
        var customEpoch = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var layout = SnowflakeBitLayout.WithEpoch(customEpoch);
        var generator = new SnowflakeIdGenerator(1, layout);

        // Act
        var id = generator.NextId();
        generator.ParseId(id, out var metadata);

        // Assert
        metadata.GeneratedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        layout.GetEpoch().Should().Be(customEpoch);
    }

    [Fact]
    public void CustomEpoch_ViaOptions_ShouldWork()
    {
        // Arrange
        var services = new ServiceCollection();
        var customEpoch = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act
        services.AddDistributedId(options =>
        {
            options.WorkerId = 5;
            options.AutoDetectWorkerId = false;
            options.CustomEpoch = customEpoch;
        });

        var provider = services.BuildServiceProvider();
        var generator = provider.GetRequiredService<IDistributedIdGenerator>() as SnowflakeIdGenerator;

        // Assert
        generator.Should().NotBeNull();
        var layout = generator!.GetLayout();
        layout.GetEpoch().Should().Be(customEpoch);
    }

    [Fact]
    public void CustomLayout_Create_ShouldWork()
    {
        // Arrange
        var customEpoch = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var layout = SnowflakeBitLayout.Create(
            epoch: customEpoch,
            timestampBits: 42,
            workerIdBits: 9,
            sequenceBits: 12);

        // Act
        var generator = new SnowflakeIdGenerator(10, layout);
        var id = generator.NextId();
        generator.ParseId(id, out var metadata);

        // Assert
        layout.TimestampBits.Should().Be(42);
        layout.WorkerIdBits.Should().Be(9);
        layout.SequenceBits.Should().Be(12);
        layout.GetEpoch().Should().Be(customEpoch);
        metadata.WorkerId.Should().Be(10);
    }

    [Fact]
    public void LockFree_Concurrent_ShouldGenerateUniqueIds()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(1);
        var ids = new System.Collections.Concurrent.ConcurrentBag<long>();

        // Act - High concurrency test
        Parallel.For(0, 50000, _ =>
        {
            ids.Add(generator.NextId());
        });

        // Assert
        var uniqueIds = new HashSet<long>(ids);
        uniqueIds.Count.Should().Be(50000, "all IDs should be unique even under extreme concurrent load");
    }

    [Fact]
    public void MultipleLayouts_ShouldWork()
    {
        // Arrange
        var epoch1 = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var epoch2 = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        
        var gen1 = new SnowflakeIdGenerator(1, SnowflakeBitLayout.WithEpoch(epoch1));
        var gen2 = new SnowflakeIdGenerator(1, SnowflakeBitLayout.WithEpoch(epoch2));

        // Act
        var id1 = gen1.NextId();
        var id2 = gen2.NextId();

        // Assert - Different epochs produce different IDs for same worker
        id1.Should().NotBe(id2);
        
        gen1.GetLayout().GetEpoch().Should().Be(epoch1);
        gen2.GetLayout().GetEpoch().Should().Be(epoch2);
    }

    [Fact]
    public void ToString_ShouldIncludeEpoch()
    {
        // Arrange
        var customEpoch = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var layout = SnowflakeBitLayout.WithEpoch(customEpoch);

        // Act
        var description = layout.ToString();

        // Assert
        description.Should().Contain("2020-01-01");
        description.Should().Contain("41-10-12");
    }

    [Fact]
    public void ZeroGC_WithCustomEpoch_ShouldWork()
    {
        // Arrange
        var customEpoch = new DateTime(2023, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var layout = SnowflakeBitLayout.Create(customEpoch);
        var generator = new SnowflakeIdGenerator(42, layout);

        // Act
        var id = generator.NextId();  // 0 bytes
        generator.ParseId(id, out var metadata);  // 0 bytes

        Span<char> buffer = stackalloc char[20];
        var success = generator.TryWriteNextId(buffer, out var len);  // 0 bytes

        // Assert
        success.Should().BeTrue();
        len.Should().BeGreaterThan(0);
        metadata.WorkerId.Should().Be(42);
    }
}

