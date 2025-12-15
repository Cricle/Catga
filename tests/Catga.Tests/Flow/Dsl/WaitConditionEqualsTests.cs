using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for WaitCondition equality and comparison
/// </summary>
public class WaitConditionEqualsTests
{
    #region Equality Tests

    [Fact]
    public void WaitCondition_SameValues_AreEqual()
    {
        var wc1 = new WaitCondition("corr-123", "EventType");
        var wc2 = new WaitCondition("corr-123", "EventType");

        wc1.Should().Be(wc2);
    }

    [Fact]
    public void WaitCondition_DifferentCorrelationId_NotEqual()
    {
        var wc1 = new WaitCondition("corr-123", "EventType");
        var wc2 = new WaitCondition("corr-456", "EventType");

        wc1.Should().NotBe(wc2);
    }

    [Fact]
    public void WaitCondition_DifferentEventType_NotEqual()
    {
        var wc1 = new WaitCondition("corr-123", "EventType1");
        var wc2 = new WaitCondition("corr-123", "EventType2");

        wc1.Should().NotBe(wc2);
    }

    #endregion

    #region Hash Code Tests

    [Fact]
    public void WaitCondition_EqualObjects_SameHashCode()
    {
        var wc1 = new WaitCondition("corr-123", "EventType");
        var wc2 = new WaitCondition("corr-123", "EventType");

        wc1.GetHashCode().Should().Be(wc2.GetHashCode());
    }

    [Fact]
    public void WaitCondition_InDictionary_Works()
    {
        var dict = new Dictionary<WaitCondition, string>
        {
            [new WaitCondition("corr-1", "Type1")] = "value1",
            [new WaitCondition("corr-2", "Type2")] = "value2"
        };

        dict.Should().HaveCount(2);
    }

    #endregion

    #region Collection Tests

    [Fact]
    public void WaitCondition_InHashSet_Works()
    {
        var set = new HashSet<WaitCondition>
        {
            new WaitCondition("corr-1", "Type1"),
            new WaitCondition("corr-2", "Type2"),
            new WaitCondition("corr-1", "Type1")
        };

        set.Should().HaveCount(2);
    }

    #endregion
}
