using Catga.DistributedId;
using FluentAssertions;

namespace Catga.Tests.Core;

/// <summary>
/// Comprehensive tests for DistributedIdOptions, IdMetadata, SnowflakeBitLayout
/// </summary>
public class DistributedIdOptionsTests
{
    #region DistributedIdOptions Tests

    [Fact]
    public void DistributedIdOptions_DefaultValues_ShouldBeCorrect()
    {
        var options = new DistributedIdOptions();
        
        options.WorkerId.Should().Be(0);
        options.AutoDetectWorkerId.Should().BeTrue();
        options.CustomEpoch.Should().BeNull();
    }

    [Fact]
    public void DistributedIdOptions_CustomValues_ShouldBeSet()
    {
        var customEpoch = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var options = new DistributedIdOptions
        {
            WorkerId = 5,
            AutoDetectWorkerId = false,
            CustomEpoch = customEpoch
        };
        
        options.WorkerId.Should().Be(5);
        options.AutoDetectWorkerId.Should().BeFalse();
        options.CustomEpoch.Should().Be(customEpoch);
    }

    [Fact]
    public void DistributedIdOptions_GetEffectiveLayout_WithoutCustomEpoch_ShouldReturnDefaultLayout()
    {
        var options = new DistributedIdOptions();
        
        var layout = options.GetEffectiveLayout();
        
        layout.TimestampBits.Should().Be(SnowflakeBitLayout.Default.TimestampBits);
    }

    [Fact]
    public void DistributedIdOptions_GetEffectiveLayout_WithCustomEpoch_ShouldUseCustomEpoch()
    {
        var customEpoch = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var options = new DistributedIdOptions
        {
            CustomEpoch = customEpoch
        };
        
        var layout = options.GetEffectiveLayout();
        
        layout.GetEpoch().Should().Be(customEpoch);
    }

    [Fact]
    public void DistributedIdOptions_Validate_WithValidOptions_ShouldNotThrow()
    {
        var options = new DistributedIdOptions
        {
            WorkerId = 5,
            AutoDetectWorkerId = false
        };
        
        var act = () => options.Validate();
        
        act.Should().NotThrow();
    }

    [Fact]
    public void DistributedIdOptions_Validate_WithInvalidWorkerId_ShouldThrow()
    {
        var options = new DistributedIdOptions
        {
            WorkerId = 1000, // Too high for default layout (max 255)
            AutoDetectWorkerId = false
        };
        
        var act = () => options.Validate();
        
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DistributedIdOptions_GetWorkerId_WithAutoDetectFalse_ShouldReturnConfiguredWorkerId()
    {
        var options = new DistributedIdOptions
        {
            WorkerId = 42,
            AutoDetectWorkerId = false
        };
        
        var workerId = options.GetWorkerId();
        
        workerId.Should().Be(42);
    }

    #endregion

    #region IdMetadata Tests

    [Fact]
    public void IdMetadata_Properties_ShouldBeSet()
    {
        var generatedAt = DateTime.UtcNow;
        var metadata = new IdMetadata
        {
            Timestamp = 12345,
            WorkerId = 5,
            Sequence = 100,
            GeneratedAt = generatedAt
        };
        
        metadata.Timestamp.Should().Be(12345);
        metadata.WorkerId.Should().Be(5);
        metadata.Sequence.Should().Be(100);
        metadata.GeneratedAt.Should().Be(generatedAt);
    }

    [Fact]
    public void IdMetadata_Equality_ShouldWorkCorrectly()
    {
        var generatedAt = DateTime.UtcNow;
        var metadata1 = new IdMetadata { Timestamp = 100, WorkerId = 5, Sequence = 10, GeneratedAt = generatedAt };
        var metadata2 = new IdMetadata { Timestamp = 100, WorkerId = 5, Sequence = 10, GeneratedAt = generatedAt };
        var metadata3 = new IdMetadata { Timestamp = 100, WorkerId = 5, Sequence = 11, GeneratedAt = generatedAt };
        
        metadata1.Should().Be(metadata2);
        metadata1.Should().NotBe(metadata3);
    }

    [Fact]
    public void IdMetadata_ToString_ShouldReturnMeaningfulString()
    {
        var generatedAt = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var metadata = new IdMetadata
        {
            Timestamp = 12345,
            WorkerId = 5,
            Sequence = 100,
            GeneratedAt = generatedAt
        };
        
        var str = metadata.ToString();
        str.Should().Contain("5");
        str.Should().Contain("100");
        str.Should().Contain("12345");
    }

    #endregion

    #region SnowflakeBitLayout Tests

    [Fact]
    public void SnowflakeBitLayout_Default_ShouldHaveCorrectValues()
    {
        var layout = SnowflakeBitLayout.Default;
        
        layout.TimestampBits.Should().Be(44);
        layout.WorkerIdBits.Should().Be(8);
        layout.SequenceBits.Should().Be(11);
    }

    [Fact]
    public void SnowflakeBitLayout_CustomValues_ShouldBeSet()
    {
        var layout = new SnowflakeBitLayout
        {
            TimestampBits = 42,
            WorkerIdBits = 10,
            SequenceBits = 11
        };
        
        layout.TimestampBits.Should().Be(42);
        layout.WorkerIdBits.Should().Be(10);
        layout.SequenceBits.Should().Be(11);
    }

    [Fact]
    public void SnowflakeBitLayout_MaxWorkerId_ShouldBeCalculatedCorrectly()
    {
        var layout = new SnowflakeBitLayout { WorkerIdBits = 8 };
        
        // 2^8 - 1 = 255
        layout.MaxWorkerId.Should().Be(255);
    }

    [Fact]
    public void SnowflakeBitLayout_MaxSequence_ShouldBeCalculatedCorrectly()
    {
        var layout = new SnowflakeBitLayout { SequenceBits = 11 };
        
        // 2^11 - 1 = 2047
        layout.MaxSequence.Should().Be(2047);
    }

    [Fact]
    public void SnowflakeBitLayout_WorkerIdShift_ShouldBeCalculatedCorrectly()
    {
        var layout = new SnowflakeBitLayout
        {
            SequenceBits = 11
        };
        
        layout.WorkerIdShift.Should().Be(11);
    }

    [Fact]
    public void SnowflakeBitLayout_TimestampShift_ShouldBeCalculatedCorrectly()
    {
        var layout = new SnowflakeBitLayout
        {
            SequenceBits = 11,
            WorkerIdBits = 8
        };
        
        layout.TimestampShift.Should().Be(19); // 11 + 8
    }

    [Fact]
    public void SnowflakeBitLayout_Default_TotalBits_ShouldBe63()
    {
        var layout = SnowflakeBitLayout.Default;
        
        var totalBits = layout.TimestampBits + layout.WorkerIdBits + layout.SequenceBits;
        totalBits.Should().Be(63); // 44 + 8 + 11 = 63 (sign bit is reserved)
    }

    [Fact]
    public void SnowflakeBitLayout_HighConcurrency_ShouldHaveCorrectValues()
    {
        var layout = SnowflakeBitLayout.HighConcurrency;
        
        layout.TimestampBits.Should().Be(39);
        layout.WorkerIdBits.Should().Be(10);
        layout.SequenceBits.Should().Be(14);
    }

    [Fact]
    public void SnowflakeBitLayout_LargeCluster_ShouldHaveCorrectValues()
    {
        var layout = SnowflakeBitLayout.LargeCluster;
        
        layout.TimestampBits.Should().Be(38);
        layout.WorkerIdBits.Should().Be(12);
        layout.SequenceBits.Should().Be(13);
    }

    [Fact]
    public void SnowflakeBitLayout_UltraLongLifespan_ShouldHaveCorrectValues()
    {
        var layout = SnowflakeBitLayout.UltraLongLifespan;
        
        layout.TimestampBits.Should().Be(46);
        layout.WorkerIdBits.Should().Be(6);
        layout.SequenceBits.Should().Be(11);
    }

    [Fact]
    public void SnowflakeBitLayout_WithEpoch_ShouldSetEpoch()
    {
        var epoch = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var layout = SnowflakeBitLayout.WithEpoch(epoch);
        
        layout.GetEpoch().Should().Be(epoch);
    }

    [Fact]
    public void SnowflakeBitLayout_Create_ShouldSetAllValues()
    {
        var epoch = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var layout = SnowflakeBitLayout.Create(epoch, 42, 10, 11);
        
        layout.TimestampBits.Should().Be(42);
        layout.WorkerIdBits.Should().Be(10);
        layout.SequenceBits.Should().Be(11);
        layout.GetEpoch().Should().Be(epoch);
    }

    [Fact]
    public void SnowflakeBitLayout_Validate_WithValidLayout_ShouldNotThrow()
    {
        var layout = SnowflakeBitLayout.Default;
        
        var act = () => layout.Validate();
        
        act.Should().NotThrow();
    }

    [Fact]
    public void SnowflakeBitLayout_Validate_WithInvalidTotalBits_ShouldThrow()
    {
        var layout = new SnowflakeBitLayout
        {
            TimestampBits = 40,
            WorkerIdBits = 10,
            SequenceBits = 10 // Total = 60, not 63
        };
        
        var act = () => layout.Validate();
        
        act.Should().Throw<ArgumentException>().WithMessage("*63*");
    }

    [Fact]
    public void SnowflakeBitLayout_ToString_ShouldReturnMeaningfulString()
    {
        var layout = SnowflakeBitLayout.Default;
        
        var str = layout.ToString();
        
        str.Should().Contain("44");
        str.Should().Contain("8");
        str.Should().Contain("11");
        str.Should().Contain("workers");
    }

    [Fact]
    public void SnowflakeBitLayout_MaxYears_ShouldBeCalculatedCorrectly()
    {
        var layout = SnowflakeBitLayout.Default;
        
        // 44 bits timestamp should give ~557 years
        layout.MaxYears.Should().BeGreaterThan(500);
    }

    [Fact]
    public void SnowflakeBitLayout_SequenceMask_ShouldEqualMaxSequence()
    {
        var layout = SnowflakeBitLayout.Default;
        
        layout.SequenceMask.Should().Be(layout.MaxSequence);
    }

    #endregion
}
