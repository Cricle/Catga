using Catga;
using Xunit;

namespace Catga.Tests.Attributes;

public class DistributedAttributesTests
{
    [Fact]
    public void IdempotentAttribute_DefaultValues()
    {
        // Arrange & Act
        var attr = new IdempotentAttribute();

        // Assert
        Assert.Null(attr.Key);
        Assert.Equal(86400, attr.TtlSeconds);
    }

    [Fact]
    public void IdempotentAttribute_CustomValues()
    {
        // Arrange & Act
        var attr = new IdempotentAttribute { Key = "{request.Id}", TtlSeconds = 3600 };

        // Assert
        Assert.Equal("{request.Id}", attr.Key);
        Assert.Equal(3600, attr.TtlSeconds);
    }

    [Fact]
    public void DistributedLockAttribute_RequiresKey()
    {
        // Arrange & Act
        var attr = new DistributedLockAttribute("resource:{id}");

        // Assert
        Assert.Equal("resource:{id}", attr.Key);
        Assert.Equal(30, attr.TimeoutSeconds);
        Assert.Equal(10, attr.WaitSeconds);
    }

    [Fact]
    public void DistributedLockAttribute_CustomTimeouts()
    {
        // Arrange & Act
        var attr = new DistributedLockAttribute("key") { TimeoutSeconds = 60, WaitSeconds = 30 };

        // Assert
        Assert.Equal("key", attr.Key);
        Assert.Equal(60, attr.TimeoutSeconds);
        Assert.Equal(30, attr.WaitSeconds);
    }

    [Fact]
    public void RetryAttribute_DefaultValues()
    {
        // Arrange & Act
        var attr = new RetryAttribute();

        // Assert
        Assert.Equal(3, attr.MaxAttempts);
        Assert.Equal(100, attr.DelayMs);
        Assert.True(attr.Exponential);
    }

    [Fact]
    public void RetryAttribute_CustomValues()
    {
        // Arrange & Act
        var attr = new RetryAttribute { MaxAttempts = 5, DelayMs = 500, Exponential = false };

        // Assert
        Assert.Equal(5, attr.MaxAttempts);
        Assert.Equal(500, attr.DelayMs);
        Assert.False(attr.Exponential);
    }

    [Fact]
    public void TimeoutAttribute_RequiresSeconds()
    {
        // Arrange & Act
        var attr = new TimeoutAttribute(30);

        // Assert
        Assert.Equal(30, attr.Seconds);
    }

    [Fact]
    public void CircuitBreakerAttribute_DefaultValues()
    {
        // Arrange & Act
        var attr = new CircuitBreakerAttribute();

        // Assert
        Assert.Equal(5, attr.FailureThreshold);
        Assert.Equal(30, attr.BreakDurationSeconds);
    }

    [Fact]
    public void CircuitBreakerAttribute_CustomValues()
    {
        // Arrange & Act
        var attr = new CircuitBreakerAttribute { FailureThreshold = 10, BreakDurationSeconds = 60 };

        // Assert
        Assert.Equal(10, attr.FailureThreshold);
        Assert.Equal(60, attr.BreakDurationSeconds);
    }

    [Fact]
    public void ShardedAttribute_RequiresKey()
    {
        // Arrange & Act
        var attr = new ShardedAttribute("{request.CustomerId}");

        // Assert
        Assert.Equal("{request.CustomerId}", attr.Key);
    }

    [Fact]
    public void LeaderOnlyAttribute_NoProperties()
    {
        // Arrange & Act
        var attr = new LeaderOnlyAttribute();

        // Assert - just verify it can be created
        Assert.NotNull(attr);
    }

    [Fact]
    public void BroadcastAttribute_NoProperties()
    {
        // Arrange & Act
        var attr = new BroadcastAttribute();

        // Assert
        Assert.NotNull(attr);
    }

    [Fact]
    public void ClusterSingletonAttribute_NoProperties()
    {
        // Arrange & Act
        var attr = new ClusterSingletonAttribute();

        // Assert
        Assert.NotNull(attr);
    }

    [Fact]
    public void Attributes_CanBeAppliedToClass()
    {
        // Arrange & Act
        var type = typeof(TestHandler);
        var attrs = type.GetCustomAttributes(false);

        // Assert
        Assert.Contains(attrs, a => a is IdempotentAttribute);
        Assert.Contains(attrs, a => a is DistributedLockAttribute);
        Assert.Contains(attrs, a => a is RetryAttribute);
        Assert.Contains(attrs, a => a is TimeoutAttribute);
    }

    [Idempotent(Key = "{request.Id}")]
    [DistributedLock("test:{request.Id}")]
    [Retry(MaxAttempts = 5)]
    [Timeout(30)]
    private class TestHandler { }
}
