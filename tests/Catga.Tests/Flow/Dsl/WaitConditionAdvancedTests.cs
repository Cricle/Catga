using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Advanced tests for WaitCondition
/// </summary>
public class WaitConditionAdvancedTests
{
    #region Construction Tests

    [Fact]
    public void WaitCondition_WithCorrelationAndType_CreatesCorrectly()
    {
        var condition = new WaitCondition("corr-123", "OrderCreated");

        condition.CorrelationId.Should().Be("corr-123");
        condition.Type.Should().Be("OrderCreated");
    }

    [Fact]
    public void WaitCondition_EmptyCorrelationId_IsValid()
    {
        var condition = new WaitCondition("", "SomeEvent");

        condition.CorrelationId.Should().BeEmpty();
    }

    [Fact]
    public void WaitCondition_EmptyType_IsValid()
    {
        var condition = new WaitCondition("corr-123", "");

        condition.Type.Should().BeEmpty();
    }

    #endregion

    #region Equality Tests

    [Fact]
    public void WaitCondition_SameValues_AreEqual()
    {
        var cond1 = new WaitCondition("corr", "type");
        var cond2 = new WaitCondition("corr", "type");

        cond1.Should().Be(cond2);
    }

    [Fact]
    public void WaitCondition_DifferentCorrelationId_AreNotEqual()
    {
        var cond1 = new WaitCondition("corr1", "type");
        var cond2 = new WaitCondition("corr2", "type");

        cond1.Should().NotBe(cond2);
    }

    [Fact]
    public void WaitCondition_DifferentType_AreNotEqual()
    {
        var cond1 = new WaitCondition("corr", "type1");
        var cond2 = new WaitCondition("corr", "type2");

        cond1.Should().NotBe(cond2);
    }

    #endregion

    #region Special Characters Tests

    [Fact]
    public void WaitCondition_SpecialCharsInCorrelationId_Works()
    {
        var condition = new WaitCondition("order:123/sub-item.v1", "Event");

        condition.CorrelationId.Should().Contain(":");
        condition.CorrelationId.Should().Contain("/");
        condition.CorrelationId.Should().Contain(".");
    }

    [Fact]
    public void WaitCondition_UnicodeInType_Works()
    {
        var condition = new WaitCondition("corr", "订单创建事件");

        condition.Type.Should().Contain("订单");
    }

    [Fact]
    public void WaitCondition_LongStrings_Works()
    {
        var longCorrelation = new string('a', 1000);
        var longType = new string('b', 1000);
        var condition = new WaitCondition(longCorrelation, longType);

        condition.CorrelationId.Length.Should().Be(1000);
        condition.Type.Length.Should().Be(1000);
    }

    #endregion

    #region Usage Pattern Tests

    [Fact]
    public void WaitCondition_CanBeUsedInDictionary()
    {
        var dict = new Dictionary<WaitCondition, string>();
        var condition = new WaitCondition("corr", "type");

        dict[condition] = "value";

        dict[new WaitCondition("corr", "type")].Should().Be("value");
    }

    [Fact]
    public void WaitCondition_CanBeUsedInHashSet()
    {
        var set = new HashSet<WaitCondition>();

        set.Add(new WaitCondition("corr1", "type1"));
        set.Add(new WaitCondition("corr2", "type2"));
        set.Add(new WaitCondition("corr1", "type1")); // Duplicate

        set.Count.Should().Be(2);
    }

    [Fact]
    public void WaitCondition_CanBeUsedInFlowSnapshot()
    {
        var condition = new WaitCondition("flow-123", "PaymentReceived");
        var snapshot = new FlowSnapshot<TestState>
        {
            FlowId = "flow-123",
            WaitCondition = condition,
            Status = DslFlowStatus.WaitingForEvent
        };

        snapshot.WaitCondition.Should().Be(condition);
    }

    #endregion

    #region Concurrent Tests

    [Fact]
    public void WaitCondition_ConcurrentCreation_AllValid()
    {
        var conditions = new System.Collections.Concurrent.ConcurrentBag<WaitCondition>();

        Parallel.For(0, 100, i =>
        {
            conditions.Add(new WaitCondition($"corr-{i}", $"type-{i}"));
        });

        conditions.Count.Should().Be(100);
        conditions.Select(c => c.CorrelationId).Distinct().Count().Should().Be(100);
    }

    #endregion

    private class TestState : BaseFlowState
    {
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }
}
