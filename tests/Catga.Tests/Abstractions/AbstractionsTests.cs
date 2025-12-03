using Catga.Abstractions;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Abstractions;

/// <summary>
/// Tests for various abstractions
/// </summary>
public class AbstractionsTests
{
    [Fact]
    public void DistributedRateLimiterOptions_ShouldHaveDefaults()
    {
        // Act
        var options = new DistributedRateLimiterOptions();

        // Assert
        options.DefaultPermitLimit.Should().BeGreaterThan(0);
        options.DefaultWindow.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void DistributedRateLimiterOptions_CanBeModified()
    {
        // Act
        var options = new DistributedRateLimiterOptions
        {
            DefaultPermitLimit = 100,
            DefaultWindow = TimeSpan.FromSeconds(60),
            KeyPrefix = "test:",
            Algorithm = RateLimitAlgorithm.TokenBucket
        };

        // Assert
        options.DefaultPermitLimit.Should().Be(100);
        options.DefaultWindow.Should().Be(TimeSpan.FromSeconds(60));
        options.KeyPrefix.Should().Be("test:");
        options.Algorithm.Should().Be(RateLimitAlgorithm.TokenBucket);
    }

    [Fact]
    public void RateLimitResult_Acquired_ShouldWork()
    {
        // Act
        var result = RateLimitResult.Acquired(remaining: 50);

        // Assert
        result.IsAcquired.Should().BeTrue();
        result.RemainingPermits.Should().Be(50);
        result.Reason.Should().Be(RateLimitRejectionReason.None);
    }

    [Fact]
    public void RateLimitResult_Rejected_ShouldWork()
    {
        // Act
        var result = RateLimitResult.Rejected(RateLimitRejectionReason.RateLimitExceeded, TimeSpan.FromSeconds(30));

        // Assert
        result.IsAcquired.Should().BeFalse();
        result.Reason.Should().Be(RateLimitRejectionReason.RateLimitExceeded);
        result.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void RateLimitStatistics_ShouldBeCreatable()
    {
        // Act
        var stats = new RateLimitStatistics
        {
            CurrentCount = 50,
            Limit = 100,
            ResetAfter = TimeSpan.FromSeconds(30)
        };

        // Assert
        stats.CurrentCount.Should().Be(50);
        stats.Limit.Should().Be(100);
        stats.ResetAfter.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void RateLimitAlgorithm_ShouldHaveExpectedValues()
    {
        // Assert
        RateLimitAlgorithm.FixedWindow.Should().BeDefined();
        RateLimitAlgorithm.SlidingWindow.Should().BeDefined();
        RateLimitAlgorithm.TokenBucket.Should().BeDefined();
    }
}
