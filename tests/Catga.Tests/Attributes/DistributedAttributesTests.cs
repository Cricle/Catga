using Catga;
using FluentAssertions;

namespace Catga.Tests.Attributes;

/// <summary>
/// Comprehensive tests for distributed attributes: RetryAttribute, TimeoutAttribute, 
/// CircuitBreakerAttribute, IdempotentAttribute, DistributedLockAttribute, etc.
/// </summary>
public class DistributedAttributesTests
{
    #region RetryAttribute Tests

    [Fact]
    public void RetryAttribute_DefaultValues_ShouldBeCorrect()
    {
        var attr = new RetryAttribute();
        
        attr.MaxAttempts.Should().Be(3);
        attr.DelayMs.Should().Be(100);
        attr.Exponential.Should().BeTrue();
    }

    [Fact]
    public void RetryAttribute_CustomValues_ShouldBeSet()
    {
        var attr = new RetryAttribute
        {
            MaxAttempts = 5,
            DelayMs = 500,
            Exponential = false
        };
        
        attr.MaxAttempts.Should().Be(5);
        attr.DelayMs.Should().Be(500);
        attr.Exponential.Should().BeFalse();
    }

    [Fact]
    public void RetryAttribute_ShouldBeApplicableToClass()
    {
        var attrUsage = typeof(RetryAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .FirstOrDefault() as AttributeUsageAttribute;
        
        attrUsage.Should().NotBeNull();
        attrUsage!.ValidOn.Should().HaveFlag(AttributeTargets.Class);
    }

    #endregion

    #region TimeoutAttribute Tests

    [Fact]
    public void TimeoutAttribute_Constructor_ShouldSetSeconds()
    {
        var attr = new TimeoutAttribute(30);
        
        attr.Seconds.Should().Be(30);
    }

    [Fact]
    public void TimeoutAttribute_ShouldBeApplicableToClass()
    {
        var attrUsage = typeof(TimeoutAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .FirstOrDefault() as AttributeUsageAttribute;
        
        attrUsage.Should().NotBeNull();
        attrUsage!.ValidOn.Should().HaveFlag(AttributeTargets.Class);
    }

    #endregion

    #region CircuitBreakerAttribute Tests

    [Fact]
    public void CircuitBreakerAttribute_DefaultValues_ShouldBeCorrect()
    {
        var attr = new CircuitBreakerAttribute();
        
        attr.FailureThreshold.Should().Be(5);
        attr.BreakDurationSeconds.Should().Be(30);
    }

    [Fact]
    public void CircuitBreakerAttribute_CustomValues_ShouldBeSet()
    {
        var attr = new CircuitBreakerAttribute
        {
            FailureThreshold = 10,
            BreakDurationSeconds = 60
        };
        
        attr.FailureThreshold.Should().Be(10);
        attr.BreakDurationSeconds.Should().Be(60);
    }

    [Fact]
    public void CircuitBreakerAttribute_ShouldBeApplicableToClass()
    {
        var attrUsage = typeof(CircuitBreakerAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .FirstOrDefault() as AttributeUsageAttribute;
        
        attrUsage.Should().NotBeNull();
        attrUsage!.ValidOn.Should().HaveFlag(AttributeTargets.Class);
    }

    #endregion

    #region IdempotentAttribute Tests

    [Fact]
    public void IdempotentAttribute_DefaultValues_ShouldBeCorrect()
    {
        var attr = new IdempotentAttribute();
        
        attr.TtlSeconds.Should().Be(86400);
        attr.Key.Should().BeNull();
    }

    [Fact]
    public void IdempotentAttribute_CustomValues_ShouldBeSet()
    {
        var attr = new IdempotentAttribute
        {
            TtlSeconds = 7200,
            Key = "custom-key"
        };
        
        attr.TtlSeconds.Should().Be(7200);
        attr.Key.Should().Be("custom-key");
    }

    [Fact]
    public void IdempotentAttribute_ShouldBeApplicableToClass()
    {
        var attrUsage = typeof(IdempotentAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .FirstOrDefault() as AttributeUsageAttribute;
        
        attrUsage.Should().NotBeNull();
        attrUsage!.ValidOn.Should().HaveFlag(AttributeTargets.Class);
    }

    #endregion

    #region DistributedLockAttribute Tests

    [Fact]
    public void DistributedLockAttribute_Constructor_ShouldSetKey()
    {
        var attr = new DistributedLockAttribute("my-lock");
        
        attr.Key.Should().Be("my-lock");
    }

    [Fact]
    public void DistributedLockAttribute_DefaultValues_ShouldBeCorrect()
    {
        var attr = new DistributedLockAttribute("my-lock");
        
        attr.TimeoutSeconds.Should().Be(30);
        attr.WaitSeconds.Should().Be(10);
    }

    [Fact]
    public void DistributedLockAttribute_CustomValues_ShouldBeSet()
    {
        var attr = new DistributedLockAttribute("my-lock")
        {
            TimeoutSeconds = 60,
            WaitSeconds = 20
        };
        
        attr.Key.Should().Be("my-lock");
        attr.TimeoutSeconds.Should().Be(60);
        attr.WaitSeconds.Should().Be(20);
    }

    [Fact]
    public void DistributedLockAttribute_ShouldBeApplicableToClass()
    {
        var attrUsage = typeof(DistributedLockAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .FirstOrDefault() as AttributeUsageAttribute;
        
        attrUsage.Should().NotBeNull();
        attrUsage!.ValidOn.Should().HaveFlag(AttributeTargets.Class);
    }

    #endregion

    #region ShardedAttribute Tests

    [Fact]
    public void ShardedAttribute_Constructor_ShouldSetKey()
    {
        var attr = new ShardedAttribute("CustomerId");
        
        attr.Key.Should().Be("CustomerId");
    }

    [Fact]
    public void ShardedAttribute_ShouldBeApplicableToClass()
    {
        var attrUsage = typeof(ShardedAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .FirstOrDefault() as AttributeUsageAttribute;
        
        attrUsage.Should().NotBeNull();
        attrUsage!.ValidOn.Should().HaveFlag(AttributeTargets.Class);
    }

    #endregion

    #region BroadcastAttribute Tests

    [Fact]
    public void BroadcastAttribute_ShouldBeApplicableToClass()
    {
        var attrUsage = typeof(BroadcastAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .FirstOrDefault() as AttributeUsageAttribute;
        
        attrUsage.Should().NotBeNull();
        attrUsage!.ValidOn.Should().HaveFlag(AttributeTargets.Class);
    }

    #endregion

    #region LeaderOnlyAttribute Tests

    [Fact]
    public void LeaderOnlyAttribute_ShouldBeApplicableToClass()
    {
        var attrUsage = typeof(LeaderOnlyAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .FirstOrDefault() as AttributeUsageAttribute;
        
        attrUsage.Should().NotBeNull();
        attrUsage!.ValidOn.Should().HaveFlag(AttributeTargets.Class);
    }

    #endregion

    #region ClusterSingletonAttribute Tests

    [Fact]
    public void ClusterSingletonAttribute_ShouldBeApplicableToClass()
    {
        var attrUsage = typeof(ClusterSingletonAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .FirstOrDefault() as AttributeUsageAttribute;
        
        attrUsage.Should().NotBeNull();
        attrUsage!.ValidOn.Should().HaveFlag(AttributeTargets.Class);
    }

    #endregion
}
