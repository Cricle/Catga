using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Advanced tests for FlowPosition - tracking nested branch paths
/// </summary>
public class FlowPositionAdvancedTests
{
    #region Construction Tests

    [Fact]
    public void FlowPosition_EmptyPath_IsValid()
    {
        var position = new FlowPosition(Array.Empty<int>());
        position.Path.Should().BeEmpty();
    }

    [Fact]
    public void FlowPosition_SingleElement_CreatesCorrectPath()
    {
        var position = new FlowPosition(new[] { 5 });
        position.Path.Should().BeEquivalentTo(new[] { 5 });
    }

    [Fact]
    public void FlowPosition_MultipleElements_CreatesCorrectPath()
    {
        var position = new FlowPosition(new[] { 1, 2, 3, 4, 5 });
        position.Path.Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 });
    }

    [Fact]
    public void FlowPosition_DeepNesting_Supported()
    {
        var path = Enumerable.Range(0, 100).ToArray();
        var position = new FlowPosition(path);
        position.Path.Should().HaveCount(100);
    }

    #endregion

    #region Equality Tests

    [Fact]
    public void FlowPosition_SamePath_AreEqual()
    {
        var pos1 = new FlowPosition(new[] { 1, 2, 3 });
        var pos2 = new FlowPosition(new[] { 1, 2, 3 });

        pos1.Should().Be(pos2);
    }

    [Fact]
    public void FlowPosition_DifferentPath_AreNotEqual()
    {
        var pos1 = new FlowPosition(new[] { 1, 2, 3 });
        var pos2 = new FlowPosition(new[] { 1, 2, 4 });

        pos1.Should().NotBe(pos2);
    }

    [Fact]
    public void FlowPosition_DifferentLength_AreNotEqual()
    {
        var pos1 = new FlowPosition(new[] { 1, 2 });
        var pos2 = new FlowPosition(new[] { 1, 2, 3 });

        pos1.Should().NotBe(pos2);
    }

    #endregion

    #region Navigation Tests

    [Fact]
    public void FlowPosition_RepresentsBranchPath()
    {
        // Position [0, 1, 2] means: step 0, branch 1, sub-step 2
        var position = new FlowPosition(new[] { 0, 1, 2 });

        position.Path[0].Should().Be(0); // Main step index
        position.Path[1].Should().Be(1); // Branch index
        position.Path[2].Should().Be(2); // Sub-step index
    }

    [Fact]
    public void FlowPosition_NestedIfBranch_RepresentsCorrectly()
    {
        // Represents: step 3 -> then branch (0) -> step 1 -> else branch (-1) -> step 0
        var position = new FlowPosition(new[] { 3, 0, 1, -1, 0 });

        position.Path.Should().HaveCount(5);
    }

    [Fact]
    public void FlowPosition_SwitchCaseBranch_RepresentsCorrectly()
    {
        // Represents: step 2 -> case 5 -> step 0
        var position = new FlowPosition(new[] { 2, 5, 0 });

        position.Path.Should().HaveCount(3);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void FlowPosition_NegativeIndices_Supported()
    {
        // Negative index like -1 can represent "else" branch
        var position = new FlowPosition(new[] { 0, -1, 0 });

        position.Path.Should().Contain(-1);
    }

    [Fact]
    public void FlowPosition_ZeroIndex_IsValid()
    {
        var position = new FlowPosition(new[] { 0, 0, 0 });

        position.Path.Should().AllBeEquivalentTo(0);
    }

    [Fact]
    public void FlowPosition_LargeIndices_Supported()
    {
        var position = new FlowPosition(new[] { int.MaxValue, 0 });

        position.Path[0].Should().Be(int.MaxValue);
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public void FlowPosition_ConcurrentCreation_AllValid()
    {
        var positions = new System.Collections.Concurrent.ConcurrentBag<FlowPosition>();

        Parallel.For(0, 100, i =>
        {
            positions.Add(new FlowPosition(new[] { i, i + 1, i + 2 }));
        });

        positions.Count.Should().Be(100);
        positions.Select(p => p.Path[0]).Distinct().Count().Should().Be(100);
    }

    #endregion
}
