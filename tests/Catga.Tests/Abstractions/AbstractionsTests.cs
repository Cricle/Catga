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
    public void LeaderElectionOptions_ShouldHaveDefaults()
    {
        // Act
        var options = new LeaderElectionOptions();

        // Assert
        options.Should().NotBeNull();
        options.LeaseDuration.Should().BeGreaterThan(TimeSpan.Zero);
        options.RenewInterval.Should().BeGreaterThan(TimeSpan.Zero);
        options.NodeId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void LeaderElectionOptions_CanBeModified()
    {
        // Act
        var options = new LeaderElectionOptions
        {
            LeaseDuration = TimeSpan.FromSeconds(30),
            RenewInterval = TimeSpan.FromSeconds(10),
            KeyPrefix = "test:leader:",
            NodeId = "test-node"
        };

        // Assert
        options.LeaseDuration.Should().Be(TimeSpan.FromSeconds(30));
        options.RenewInterval.Should().Be(TimeSpan.FromSeconds(10));
        options.KeyPrefix.Should().Be("test:leader:");
        options.NodeId.Should().Be("test-node");
    }

    [Fact]
    public void LeadershipChange_ShouldBeCreatable()
    {
        // Act
        var change = new LeadershipChange
        {
            Type = LeadershipChangeType.Elected,
            NewLeader = new LeaderInfo { NodeId = "node-1" },
            Timestamp = DateTimeOffset.UtcNow
        };

        // Assert
        change.Type.Should().Be(LeadershipChangeType.Elected);
        change.NewLeader.Should().NotBeNull();
    }

    [Fact]
    public void LeadershipChange_Lost_ShouldWork()
    {
        // Act
        var change = new LeadershipChange
        {
            Type = LeadershipChangeType.Lost,
            PreviousLeader = new LeaderInfo { NodeId = "node-2" },
            Timestamp = DateTimeOffset.UtcNow
        };

        // Assert
        change.Type.Should().Be(LeadershipChangeType.Lost);
        change.PreviousLeader.Should().NotBeNull();
    }

    [Fact]
    public void LeaderInfo_ShouldBeCreatable()
    {
        // Act
        var info = new LeaderInfo
        {
            NodeId = "leader-node",
            AcquiredAt = DateTimeOffset.UtcNow,
            Endpoint = "http://localhost:8080"
        };

        // Assert
        info.NodeId.Should().Be("leader-node");
        info.Endpoint.Should().Be("http://localhost:8080");
    }

    [Fact]
    public void RateLimitAlgorithm_ShouldHaveExpectedValues()
    {
        // Assert
        RateLimitAlgorithm.FixedWindow.Should().BeDefined();
        RateLimitAlgorithm.SlidingWindow.Should().BeDefined();
        RateLimitAlgorithm.TokenBucket.Should().BeDefined();
    }

    [Fact]
    public void LeadershipChangeType_ShouldHaveExpectedValues()
    {
        // Assert
        LeadershipChangeType.Elected.Should().BeDefined();
        LeadershipChangeType.Resigned.Should().BeDefined();
        LeadershipChangeType.Lost.Should().BeDefined();
    }
}
