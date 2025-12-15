using System.Diagnostics;
using Catga.Observability;
using FluentAssertions;

namespace Catga.Tests.Observability;

/// <summary>
/// Comprehensive tests for FlowActivitySource
/// </summary>
public class FlowActivitySourceTests
{
    [Fact]
    public void Source_ShouldHaveCorrectName()
    {
        FlowActivitySource.SourceName.Should().Be("Catga.Flow");
    }

    [Fact]
    public void Source_ShouldHaveCorrectVersion()
    {
        FlowActivitySource.Version.Should().Be("1.0.0");
    }

    [Fact]
    public void Source_ShouldNotBeNull()
    {
        FlowActivitySource.Source.Should().NotBeNull();
    }

    [Fact]
    public void Source_ShouldHaveCorrectNameProperty()
    {
        FlowActivitySource.Source.Name.Should().Be("Catga.Flow");
    }

    #region Tags Tests

    [Fact]
    public void Tags_FlowName_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Tags.FlowName.Should().Be("catga.flow.name");
    }

    [Fact]
    public void Tags_FlowId_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Tags.FlowId.Should().Be("catga.flow.id");
    }

    [Fact]
    public void Tags_FlowStatus_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Tags.FlowStatus.Should().Be("catga.flow.status");
    }

    [Fact]
    public void Tags_StepIndex_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Tags.StepIndex.Should().Be("catga.flow.step.index");
    }

    [Fact]
    public void Tags_StepType_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Tags.StepType.Should().Be("catga.flow.step.type");
    }

    [Fact]
    public void Tags_StepTag_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Tags.StepTag.Should().Be("catga.flow.step.tag");
    }

    [Fact]
    public void Tags_StepStatus_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Tags.StepStatus.Should().Be("catga.flow.step.status");
    }

    [Fact]
    public void Tags_BranchType_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Tags.BranchType.Should().Be("catga.flow.branch.type");
    }

    [Fact]
    public void Tags_BranchIndex_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Tags.BranchIndex.Should().Be("catga.flow.branch.index");
    }

    [Fact]
    public void Tags_ForEachItemIndex_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Tags.ForEachItemIndex.Should().Be("catga.flow.foreach.item_index");
    }

    [Fact]
    public void Tags_ForEachTotalItems_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Tags.ForEachTotalItems.Should().Be("catga.flow.foreach.total_items");
    }

    [Fact]
    public void Tags_ForEachParallelism_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Tags.ForEachParallelism.Should().Be("catga.flow.foreach.parallelism");
    }

    [Fact]
    public void Tags_Error_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Tags.Error.Should().Be("catga.flow.error");
    }

    [Fact]
    public void Tags_ErrorType_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Tags.ErrorType.Should().Be("catga.flow.error.type");
    }

    [Fact]
    public void Tags_Duration_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Tags.Duration.Should().Be("catga.flow.duration.ms");
    }

    [Fact]
    public void Tags_RetryCount_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Tags.RetryCount.Should().Be("catga.flow.retry.count");
    }

    #endregion

    #region Events Tests

    [Fact]
    public void Events_FlowStarted_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Events.FlowStarted.Should().Be("catga.flow.started");
    }

    [Fact]
    public void Events_FlowCompleted_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Events.FlowCompleted.Should().Be("catga.flow.completed");
    }

    [Fact]
    public void Events_FlowFailed_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Events.FlowFailed.Should().Be("catga.flow.failed");
    }

    [Fact]
    public void Events_FlowResumed_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Events.FlowResumed.Should().Be("catga.flow.resumed");
    }

    [Fact]
    public void Events_StepStarted_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Events.StepStarted.Should().Be("catga.flow.step.started");
    }

    [Fact]
    public void Events_StepCompleted_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Events.StepCompleted.Should().Be("catga.flow.step.completed");
    }

    [Fact]
    public void Events_StepFailed_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Events.StepFailed.Should().Be("catga.flow.step.failed");
    }

    [Fact]
    public void Events_StepSkipped_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Events.StepSkipped.Should().Be("catga.flow.step.skipped");
    }

    [Fact]
    public void Events_StepRetried_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Events.StepRetried.Should().Be("catga.flow.step.retried");
    }

    [Fact]
    public void Events_BranchEntered_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Events.BranchEntered.Should().Be("catga.flow.branch.entered");
    }

    [Fact]
    public void Events_BranchExited_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Events.BranchExited.Should().Be("catga.flow.branch.exited");
    }

    [Fact]
    public void Events_ForEachStarted_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Events.ForEachStarted.Should().Be("catga.flow.foreach.started");
    }

    [Fact]
    public void Events_ForEachItemProcessed_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Events.ForEachItemProcessed.Should().Be("catga.flow.foreach.item_processed");
    }

    [Fact]
    public void Events_ForEachCompleted_ShouldHaveCorrectValue()
    {
        FlowActivitySource.Events.ForEachCompleted.Should().Be("catga.flow.foreach.completed");
    }

    #endregion

    #region Activity Helper Tests

    [Fact]
    public void StartFlowActivity_ShouldNotThrow()
    {
        var act = () => FlowActivitySource.StartFlowActivity("TestFlow", "flow-123");
        act.Should().NotThrow();
    }

    [Fact]
    public void StartFlowActivity_WithNullFlowId_ShouldNotThrow()
    {
        var act = () => FlowActivitySource.StartFlowActivity("TestFlow");
        act.Should().NotThrow();
    }

    [Fact]
    public void StartStepActivity_ShouldNotThrow()
    {
        var act = () => FlowActivitySource.StartStepActivity("TestFlow", 0, "Send");
        act.Should().NotThrow();
    }

    #endregion
}
