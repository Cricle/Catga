using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Core;

/// <summary>
/// Advanced tests for BaseFlowState
/// </summary>
public class BaseFlowStateAdvancedTests
{
    private class TestState : BaseFlowState
    {
        public string Data { get; set; } = "";
        public int Counter { get; set; }
        public List<string> Items { get; set; } = new();
        public DateTime LastUpdated { get; set; }

        public override IEnumerable<string> GetChangedFieldNames()
        {
            var changed = new List<string>();
            if (!string.IsNullOrEmpty(Data)) changed.Add(nameof(Data));
            if (Counter > 0) changed.Add(nameof(Counter));
            if (Items.Any()) changed.Add(nameof(Items));
            return changed;
        }
    }

    #region FlowId Tests

    [Fact]
    public void FlowId_CanBeSet()
    {
        var state = new TestState { FlowId = "test-flow-123" };
        state.FlowId.Should().Be("test-flow-123");
    }

    [Fact]
    public void FlowId_EmptyString_IsValid()
    {
        var state = new TestState { FlowId = "" };
        state.FlowId.Should().BeEmpty();
    }

    [Fact]
    public void FlowId_SpecialCharacters_Works()
    {
        var state = new TestState { FlowId = "flow:with/special-chars_日本語" };
        state.FlowId.Should().Contain("日本語");
    }

    #endregion

    #region Changed Fields Tests

    [Fact]
    public void GetChangedFieldNames_NoChanges_ReturnsEmpty()
    {
        var state = new TestState();
        state.GetChangedFieldNames().Should().BeEmpty();
    }

    [Fact]
    public void GetChangedFieldNames_DataSet_ReturnsDataField()
    {
        var state = new TestState { Data = "test" };
        state.GetChangedFieldNames().Should().Contain(nameof(TestState.Data));
    }

    [Fact]
    public void GetChangedFieldNames_MultipleChanges_ReturnsAll()
    {
        var state = new TestState
        {
            Data = "test",
            Counter = 5,
            Items = new List<string> { "item1" }
        };

        var changed = state.GetChangedFieldNames().ToList();
        changed.Should().Contain(nameof(TestState.Data));
        changed.Should().Contain(nameof(TestState.Counter));
        changed.Should().Contain(nameof(TestState.Items));
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public void State_ConcurrentCreation_AllValid()
    {
        var states = new System.Collections.Concurrent.ConcurrentBag<TestState>();

        Parallel.For(0, 100, i =>
        {
            states.Add(new TestState
            {
                FlowId = $"flow-{i}",
                Data = $"data-{i}",
                Counter = i
            });
        });

        states.Count.Should().Be(100);
        states.Select(s => s.FlowId).Distinct().Count().Should().Be(100);
    }

    [Fact]
    public void State_ConcurrentPropertyAccess_NoExceptions()
    {
        var state = new TestState { FlowId = "shared", Data = "shared-data" };
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, 100, i =>
        {
            try
            {
                _ = state.FlowId;
                _ = state.Data;
                _ = state.GetChangedFieldNames();
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        exceptions.Should().BeEmpty();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void State_DefaultValues_AreCorrect()
    {
        var state = new TestState();

        state.FlowId.Should().BeNull();
        state.Data.Should().BeEmpty();
        state.Counter.Should().Be(0);
        state.Items.Should().BeEmpty();
    }

    [Fact]
    public void State_LargeData_Works()
    {
        var largeData = new string('x', 100000);
        var state = new TestState { Data = largeData };

        state.Data.Length.Should().Be(100000);
    }

    [Fact]
    public void State_LargeCollection_Works()
    {
        var state = new TestState
        {
            Items = Enumerable.Range(0, 10000).Select(i => $"item-{i}").ToList()
        };

        state.Items.Should().HaveCount(10000);
    }

    #endregion
}
