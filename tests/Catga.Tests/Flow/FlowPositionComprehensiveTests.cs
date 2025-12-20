using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow;

/// <summary>
/// Comprehensive tests for FlowPosition, BranchType, ForEachFailureHandling, ForEachProgress
/// </summary>
public class FlowPositionComprehensiveTests
{
    #region FlowPosition Tests

    [Fact]
    public void FlowPosition_Initial_ShouldBeAtStepZero()
    {
        var position = FlowPosition.Initial;
        
        position.CurrentIndex.Should().Be(0);
        position.Depth.Should().Be(0);
        position.IsInBranch.Should().BeFalse();
        position.Path.Should().BeEquivalentTo([0]);
    }

    [Fact]
    public void FlowPosition_Constructor_ShouldSetPath()
    {
        var position = new FlowPosition([1, 2, 3]);
        
        position.Path.Should().BeEquivalentTo([1, 2, 3]);
        position.CurrentIndex.Should().Be(3);
        position.Depth.Should().Be(2);
        position.IsInBranch.Should().BeTrue();
    }

    [Fact]
    public void FlowPosition_Constructor_WithNullPath_ShouldDefaultToZero()
    {
        var position = new FlowPosition(null!);
        
        position.Path.Should().BeEquivalentTo([0]);
    }

    [Fact]
    public void FlowPosition_Advance_ShouldIncrementCurrentIndex()
    {
        var position = new FlowPosition([0]);
        
        var advanced = position.Advance();
        
        advanced.CurrentIndex.Should().Be(1);
        advanced.Path.Should().BeEquivalentTo([1]);
    }

    [Fact]
    public void FlowPosition_Advance_InBranch_ShouldOnlyIncrementLastElement()
    {
        var position = new FlowPosition([1, 2, 3]);
        
        var advanced = position.Advance();
        
        advanced.Path.Should().BeEquivalentTo([1, 2, 4]);
    }

    [Fact]
    public void FlowPosition_Advance_EmptyPath_ShouldReturnOne()
    {
        var position = new FlowPosition([]);
        
        var advanced = position.Advance();
        
        advanced.Path.Should().BeEquivalentTo([1]);
    }

    [Fact]
    public void FlowPosition_EnterBranch_ShouldAddNewLevel()
    {
        var position = new FlowPosition([0]);
        
        var inBranch = position.EnterBranch(5);
        
        inBranch.Path.Should().BeEquivalentTo([0, 5]);
        inBranch.IsInBranch.Should().BeTrue();
        inBranch.Depth.Should().Be(1);
    }

    [Fact]
    public void FlowPosition_EnterBranch_Nested_ShouldAddMultipleLevels()
    {
        var position = new FlowPosition([0])
            .EnterBranch(1)
            .EnterBranch(2);
        
        position.Path.Should().BeEquivalentTo([0, 1, 2]);
        position.Depth.Should().Be(2);
    }

    [Fact]
    public void FlowPosition_ExitBranch_ShouldRemoveLastLevel()
    {
        var position = new FlowPosition([0, 1, 2]);
        
        var exited = position.ExitBranch();
        
        exited.Path.Should().BeEquivalentTo([0, 1]);
        exited.Depth.Should().Be(1);
    }

    [Fact]
    public void FlowPosition_ExitBranch_AtTopLevel_ShouldReturnSame()
    {
        var position = new FlowPosition([0]);
        
        var exited = position.ExitBranch();
        
        exited.Path.Should().BeEquivalentTo([0]);
    }

    [Fact]
    public void FlowPosition_Parent_ShouldReturnParentPosition()
    {
        var position = new FlowPosition([0, 1, 2]);
        
        var parent = position.Parent;
        
        parent.Path.Should().BeEquivalentTo([0, 1]);
    }

    [Fact]
    public void FlowPosition_CurrentIndex_EmptyPath_ShouldReturnZero()
    {
        var position = new FlowPosition([]);
        
        position.CurrentIndex.Should().Be(0);
    }

    #endregion

    #region BranchType Tests

    [Fact]
    public void BranchType_ShouldHaveCorrectValues()
    {
        BranchType.Then.Should().Be(BranchType.Then);
        BranchType.Else.Should().Be(BranchType.Else);
        BranchType.Case.Should().Be(BranchType.Case);
        BranchType.Default.Should().Be(BranchType.Default);
    }

    [Fact]
    public void BranchType_AllValues_ShouldBeDistinct()
    {
        var values = Enum.GetValues<BranchType>();
        values.Should().OnlyHaveUniqueItems();
    }

    #endregion

    #region ForEachFailureHandling Tests

    [Fact]
    public void ForEachFailureHandling_ShouldHaveCorrectValues()
    {
        ForEachFailureHandling.StopOnFirstFailure.Should().Be(ForEachFailureHandling.StopOnFirstFailure);
        ForEachFailureHandling.ContinueOnFailure.Should().Be(ForEachFailureHandling.ContinueOnFailure);
    }

    #endregion

    #region ForEachProgress Tests

    [Fact]
    public void ForEachProgress_DefaultValues_ShouldBeCorrect()
    {
        var progress = new ForEachProgress();
        
        progress.CurrentIndex.Should().Be(0);
        progress.TotalCount.Should().Be(0);
        progress.CompletedIndices.Should().BeEmpty();
        progress.FailedIndices.Should().BeEmpty();
    }

    [Fact]
    public void ForEachProgress_WithValues_ShouldSetCorrectly()
    {
        var progress = new ForEachProgress
        {
            CurrentIndex = 5,
            TotalCount = 10,
            CompletedIndices = [0, 1, 2, 3, 4],
            FailedIndices = [6, 7]
        };
        
        progress.CurrentIndex.Should().Be(5);
        progress.TotalCount.Should().Be(10);
        progress.CompletedIndices.Should().BeEquivalentTo([0, 1, 2, 3, 4]);
        progress.FailedIndices.Should().BeEquivalentTo([6, 7]);
    }

    [Fact]
    public void ForEachProgress_CompletedIndices_ShouldBeMutable()
    {
        var progress = new ForEachProgress();
        
        progress.CompletedIndices.Add(1);
        progress.CompletedIndices.Add(2);
        
        progress.CompletedIndices.Should().BeEquivalentTo([1, 2]);
    }

    [Fact]
    public void ForEachProgress_FailedIndices_ShouldBeMutable()
    {
        var progress = new ForEachProgress();
        
        progress.FailedIndices.Add(3);
        progress.FailedIndices.Add(5);
        
        progress.FailedIndices.Should().BeEquivalentTo([3, 5]);
    }

    #endregion
}
