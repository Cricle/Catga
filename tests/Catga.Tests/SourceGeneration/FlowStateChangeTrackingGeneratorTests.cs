using Catga.Flow;
using Xunit;

namespace Catga.Tests.SourceGeneration;

public class FlowStateChangeTrackingGeneratorTests
{
    [Fact]
    public void FlowState_WithNoChanges_HasChangesFalse()
    {
        var state = new GeneratedTestFlowState();
        state.ClearChanges();

        Assert.False(state.HasChanges);
    }

    [Fact]
    public void FlowState_AfterPropertySet_HasChangesTrue()
    {
        var state = new GeneratedTestFlowState();
        state.ClearChanges();

        state.Name = "test";

        Assert.True(state.HasChanges);
    }

    [Fact]
    public void FlowState_AfterPropertySet_GetChangedFieldNamesContainsField()
    {
        var state = new GeneratedTestFlowState();
        state.ClearChanges();

        state.Name = "test";

        var changedFields = state.GetChangedFieldNames().ToList();
        Assert.Contains("Name", changedFields);
    }

    [Fact]
    public void FlowState_AfterClearChanges_HasChangesFalse()
    {
        var state = new GeneratedTestFlowState();
        state.Name = "test";
        Assert.True(state.HasChanges);

        state.ClearChanges();

        Assert.False(state.HasChanges);
    }

    [Fact]
    public void FlowState_WithMultipleFieldChanges_TracksAll()
    {
        var state = new GeneratedTestFlowState();
        state.ClearChanges();

        state.Name = "test";
        state.Value = 42;

        var changedFields = state.GetChangedFieldNames().ToList();
        Assert.Contains("Name", changedFields);
        Assert.Contains("Value", changedFields);
    }

    [Fact]
    public void FlowState_SameValueSet_DoesNotMarkChanged()
    {
        var state = new GeneratedTestFlowState { Name = "original" };
        state.ClearChanges();

        state.Name = "original";

        Assert.False(state.HasChanges);
    }

    [Fact]
    public void FlowState_DifferentValueSet_MarksChanged()
    {
        var state = new GeneratedTestFlowState { Name = "original" };
        state.ClearChanges();

        state.Name = "modified";

        Assert.True(state.HasChanges);
        Assert.Contains("Name", state.GetChangedFieldNames());
    }

    [Fact]
    public void FlowState_GetChangedMask_ReturnsCorrectBits()
    {
        var state = new GeneratedTestFlowState();
        state.ClearChanges();

        state.Name = "test";

        var mask = state.GetChangedMask();
        Assert.NotEqual(0, mask);
    }

    [Fact]
    public void FlowState_IsFieldChanged_ReturnsTrueForChangedField()
    {
        var state = new GeneratedTestFlowState();
        state.ClearChanges();

        state.Name = "test";

        Assert.True(state.IsFieldChanged(0));
    }

    [Fact]
    public void FlowState_MarkChanged_ManuallyMarksField()
    {
        var state = new GeneratedTestFlowState();
        state.ClearChanges();

        state.MarkChanged(0);

        Assert.True(state.HasChanges);
    }
}
