using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow;

public class BaseFlowStateTests
{
    private class TestState : BaseFlowState
    {
        private string _name = "";
        private int _count;

        public string Name
        {
            get => _name;
            set { _name = value; MarkChanged(0); }
        }

        public int Count
        {
            get => _count;
            set { _count = value; MarkChanged(1); }
        }

        public override IEnumerable<string> GetChangedFieldNames()
        {
            if (IsFieldChanged(0)) yield return nameof(Name);
            if (IsFieldChanged(1)) yield return nameof(Count);
        }
    }

    [Fact]
    public void FlowId_DefaultIsEmpty()
    {
        var state = new TestState();
        state.FlowId.Should().BeEmpty();
    }

    [Fact]
    public void FlowId_CanBeSet()
    {
        var state = new TestState { FlowId = "flow-123" };
        state.FlowId.Should().Be("flow-123");
    }

    [Fact]
    public void HasChanges_InitiallyFalse()
    {
        var state = new TestState();
        state.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void HasChanges_AfterModification_IsTrue()
    {
        var state = new TestState();
        state.Name = "test";
        state.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void ClearChanges_ResetsHasChanges()
    {
        var state = new TestState();
        state.Name = "test";
        state.ClearChanges();
        state.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void IsFieldChanged_TracksIndividualFields()
    {
        var state = new TestState();
        state.Name = "test";

        state.IsFieldChanged(0).Should().BeTrue();
        state.IsFieldChanged(1).Should().BeFalse();
    }

    [Fact]
    public void GetChangedMask_ReturnsCorrectMask()
    {
        var state = new TestState();
        state.Name = "test";
        state.Count = 5;

        var mask = state.GetChangedMask();
        mask.Should().Be(3); // Binary: 11
    }

    [Fact]
    public void GetChangedFieldNames_ReturnsChangedFields()
    {
        var state = new TestState();
        state.Name = "test";

        var changedFields = state.GetChangedFieldNames().ToList();
        changedFields.Should().Contain("Name");
        changedFields.Should().NotContain("Count");
    }

    [Fact]
    public void MultipleChanges_TrackAllFields()
    {
        var state = new TestState();
        state.Name = "test";
        state.Count = 10;

        var changedFields = state.GetChangedFieldNames().ToList();
        changedFields.Should().HaveCount(2);
        changedFields.Should().Contain("Name");
        changedFields.Should().Contain("Count");
    }

    [Fact]
    public void ClearChanges_ClearsAllFields()
    {
        var state = new TestState();
        state.Name = "test";
        state.Count = 10;
        state.ClearChanges();

        state.IsFieldChanged(0).Should().BeFalse();
        state.IsFieldChanged(1).Should().BeFalse();
        state.GetChangedMask().Should().Be(0);
    }
}
