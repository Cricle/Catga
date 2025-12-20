using Catga.Abstractions;
using FluentAssertions;

namespace Catga.Tests.Abstractions;

/// <summary>
/// Comprehensive tests for BatchKeyAttribute, BatchOptionsAttribute, TraceTagAttribute, TraceTagsAttribute
/// </summary>
public class AttributeComprehensiveTests
{
    #region BatchKeyAttribute Tests

    [Fact]
    public void BatchKeyAttribute_Constructor_ShouldSetPropertyName()
    {
        var attr = new BatchKeyAttribute("OrderId");
        
        attr.PropertyName.Should().Be("OrderId");
    }

    [Fact]
    public void BatchKeyAttribute_WithEmptyPropertyName_ShouldSetEmpty()
    {
        var attr = new BatchKeyAttribute("");
        
        attr.PropertyName.Should().BeEmpty();
    }

    [Fact]
    public void BatchKeyAttribute_ShouldBeApplicableToClass()
    {
        var attrUsage = typeof(BatchKeyAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .FirstOrDefault() as AttributeUsageAttribute;
        
        attrUsage.Should().NotBeNull();
        attrUsage!.ValidOn.Should().HaveFlag(AttributeTargets.Class);
    }

    #endregion

    #region BatchOptionsAttribute Tests

    [Fact]
    public void BatchOptionsAttribute_DefaultValues_ShouldBeZero()
    {
        var attr = new BatchOptionsAttribute();
        
        attr.MaxBatchSize.Should().Be(0);
        attr.BatchTimeoutMs.Should().Be(0);
        attr.MaxQueueLength.Should().Be(0);
        attr.ShardIdleTtlMs.Should().Be(0);
        attr.MaxShards.Should().Be(0);
        attr.FlushDegree.Should().Be(0);
    }

    [Fact]
    public void BatchOptionsAttribute_CustomValues_ShouldBeSet()
    {
        var attr = new BatchOptionsAttribute
        {
            MaxBatchSize = 500,
            BatchTimeoutMs = 200,
            MaxQueueLength = 1000,
            ShardIdleTtlMs = 5000,
            MaxShards = 10,
            FlushDegree = 4
        };
        
        attr.MaxBatchSize.Should().Be(500);
        attr.BatchTimeoutMs.Should().Be(200);
        attr.MaxQueueLength.Should().Be(1000);
        attr.ShardIdleTtlMs.Should().Be(5000);
        attr.MaxShards.Should().Be(10);
        attr.FlushDegree.Should().Be(4);
    }

    [Fact]
    public void BatchOptionsAttribute_ShouldBeApplicableToClass()
    {
        var attrUsage = typeof(BatchOptionsAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .FirstOrDefault() as AttributeUsageAttribute;
        
        attrUsage.Should().NotBeNull();
        attrUsage!.ValidOn.Should().HaveFlag(AttributeTargets.Class);
    }

    [Fact]
    public void BatchOptionsAttribute_ShouldNotAllowMultiple()
    {
        var attrUsage = typeof(BatchOptionsAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .FirstOrDefault() as AttributeUsageAttribute;
        
        attrUsage.Should().NotBeNull();
        attrUsage!.AllowMultiple.Should().BeFalse();
    }

    [Fact]
    public void BatchOptionsAttribute_ShouldNotBeInherited()
    {
        var attrUsage = typeof(BatchOptionsAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .FirstOrDefault() as AttributeUsageAttribute;
        
        attrUsage.Should().NotBeNull();
        attrUsage!.Inherited.Should().BeFalse();
    }

    #endregion

    #region TraceTagAttribute Tests

    [Fact]
    public void TraceTagAttribute_DefaultConstructor_ShouldHaveNullName()
    {
        var attr = new TraceTagAttribute();
        
        attr.Name.Should().BeNull();
    }

    [Fact]
    public void TraceTagAttribute_WithCustomName_ShouldSetName()
    {
        var attr = new TraceTagAttribute("custom.tag.name");
        
        attr.Name.Should().Be("custom.tag.name");
    }

    [Fact]
    public void TraceTagAttribute_WithEmptyName_ShouldSetEmpty()
    {
        var attr = new TraceTagAttribute("");
        
        attr.Name.Should().BeEmpty();
    }

    [Fact]
    public void TraceTagAttribute_ShouldBeApplicableToProperty()
    {
        var attrUsage = typeof(TraceTagAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .FirstOrDefault() as AttributeUsageAttribute;
        
        attrUsage.Should().NotBeNull();
        attrUsage!.ValidOn.Should().HaveFlag(AttributeTargets.Property);
    }

    [Fact]
    public void TraceTagAttribute_ShouldNotAllowMultiple()
    {
        var attrUsage = typeof(TraceTagAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .FirstOrDefault() as AttributeUsageAttribute;
        
        attrUsage.Should().NotBeNull();
        attrUsage!.AllowMultiple.Should().BeFalse();
    }

    [Fact]
    public void TraceTagAttribute_ShouldBeInherited()
    {
        var attrUsage = typeof(TraceTagAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .FirstOrDefault() as AttributeUsageAttribute;
        
        attrUsage.Should().NotBeNull();
        attrUsage!.Inherited.Should().BeTrue();
    }

    #endregion

    #region TraceTagsAttribute Tests

    [Fact]
    public void TraceTagsAttribute_DefaultValues_ShouldBeCorrect()
    {
        var attr = new TraceTagsAttribute();
        
        attr.Prefix.Should().BeNull();
        attr.AllPublic.Should().BeTrue();
        attr.Include.Should().BeNull();
        attr.Exclude.Should().BeNull();
    }

    [Fact]
    public void TraceTagsAttribute_WithPrefix_ShouldSetPrefix()
    {
        var attr = new TraceTagsAttribute("custom.prefix.");
        
        attr.Prefix.Should().Be("custom.prefix.");
    }

    [Fact]
    public void TraceTagsAttribute_WithInclude_ShouldSetInclude()
    {
        var attr = new TraceTagsAttribute
        {
            Include = ["OrderId", "CustomerId"]
        };
        
        attr.Include.Should().BeEquivalentTo(["OrderId", "CustomerId"]);
    }

    [Fact]
    public void TraceTagsAttribute_WithExclude_ShouldSetExclude()
    {
        var attr = new TraceTagsAttribute
        {
            Exclude = ["Password", "Secret"]
        };
        
        attr.Exclude.Should().BeEquivalentTo(["Password", "Secret"]);
    }

    [Fact]
    public void TraceTagsAttribute_ShouldBeApplicableToClass()
    {
        var attrUsage = typeof(TraceTagsAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .FirstOrDefault() as AttributeUsageAttribute;
        
        attrUsage.Should().NotBeNull();
        attrUsage!.ValidOn.Should().HaveFlag(AttributeTargets.Class);
    }

    #endregion

    #region IPrioritizedMessage Tests

    public record TestPrioritizedMessage : IPrioritizedMessage
    {
        public long MessageId { get; init; }
        public MessagePriority Priority { get; init; }
    }

    [Fact]
    public void IPrioritizedMessage_DefaultPriority_ShouldBeLow()
    {
        IPrioritizedMessage msg = new TestPrioritizedMessage { MessageId = 1 };
        
        msg.Priority.Should().Be(MessagePriority.Low);
    }

    [Fact]
    public void IPrioritizedMessage_WithCustomPriority_ShouldBeSet()
    {
        var msg = new TestPrioritizedMessage
        {
            MessageId = 1,
            Priority = MessagePriority.Critical
        };
        
        msg.Priority.Should().Be(MessagePriority.Critical);
    }

    [Theory]
    [InlineData(MessagePriority.Low)]
    [InlineData(MessagePriority.Normal)]
    [InlineData(MessagePriority.High)]
    [InlineData(MessagePriority.Critical)]
    public void IPrioritizedMessage_AllPriorities_ShouldWork(MessagePriority priority)
    {
        var msg = new TestPrioritizedMessage
        {
            MessageId = 1,
            Priority = priority
        };
        
        msg.Priority.Should().Be(priority);
    }

    #endregion
}
