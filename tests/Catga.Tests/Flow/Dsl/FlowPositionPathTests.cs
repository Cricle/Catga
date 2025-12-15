using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowPosition path tracking
/// </summary>
public class FlowPositionPathTests
{
    #region Path Creation Tests

    [Fact]
    public void FlowPosition_WithSimplePath_Works()
    {
        var position = new FlowPosition(new[] { 0 });

        position.Path.Should().Equal(0);
    }

    [Fact]
    public void FlowPosition_WithNestedPath_Works()
    {
        var position = new FlowPosition(new[] { 1, 2, 3 });

        position.Path.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void FlowPosition_WithEmptyPath_Works()
    {
        var position = new FlowPosition(new int[] { });

        position.Path.Should().BeEmpty();
    }

    #endregion

    #region Path Comparison Tests

    [Fact]
    public void FlowPosition_EqualPaths_AreEqual()
    {
        var pos1 = new FlowPosition(new[] { 1, 2, 3 });
        var pos2 = new FlowPosition(new[] { 1, 2, 3 });

        pos1.Should().Be(pos2);
    }

    [Fact]
    public void FlowPosition_DifferentPaths_AreNotEqual()
    {
        var pos1 = new FlowPosition(new[] { 1, 2, 3 });
        var pos2 = new FlowPosition(new[] { 1, 2, 4 });

        pos1.Should().NotBe(pos2);
    }

    #endregion

    #region Path Navigation Tests

    [Fact]
    public void FlowPosition_RootPath_HasSingleElement()
    {
        var position = new FlowPosition(new[] { 0 });

        position.Path.Should().HaveCount(1);
    }

    [Fact]
    public void FlowPosition_DeepPath_HasMultipleElements()
    {
        var position = new FlowPosition(new[] { 0, 1, 2, 3, 4, 5 });

        position.Path.Should().HaveCount(6);
    }

    #endregion

    #region Concurrent Path Tests

    [Fact]
    public void FlowPosition_ConcurrentCreation_AllValid()
    {
        var positions = new System.Collections.Concurrent.ConcurrentBag<FlowPosition>();

        Parallel.For(0, 100, i =>
        {
            positions.Add(new FlowPosition(new[] { i, i + 1, i + 2 }));
        });

        positions.Count.Should().Be(100);
    }

    #endregion
}
