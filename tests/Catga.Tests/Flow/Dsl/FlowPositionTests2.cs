using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Additional tests for FlowPosition
/// </summary>
public class FlowPositionTests2
{
    #region Construction Tests

    [Fact]
    public void FlowPosition_CanBeCreatedWithPath()
    {
        var position = new FlowPosition(new[] { 0, 1, 2 });

        position.Path.Should().Equal(0, 1, 2);
    }

    [Fact]
    public void FlowPosition_EmptyPath()
    {
        var position = new FlowPosition(Array.Empty<int>());

        position.Path.Should().BeEmpty();
    }

    [Fact]
    public void FlowPosition_SingleElementPath()
    {
        var position = new FlowPosition(new[] { 5 });

        position.Path.Should().Equal(5);
    }

    #endregion

    #region Equality Tests

    [Fact]
    public void FlowPosition_EqualPositions()
    {
        var pos1 = new FlowPosition(new[] { 0, 1 });
        var pos2 = new FlowPosition(new[] { 0, 1 });

        pos1.Should().Be(pos2);
    }

    [Fact]
    public void FlowPosition_DifferentPositions()
    {
        var pos1 = new FlowPosition(new[] { 0, 1 });
        var pos2 = new FlowPosition(new[] { 0, 2 });

        pos1.Should().NotBe(pos2);
    }

    [Fact]
    public void FlowPosition_DifferentLengths()
    {
        var pos1 = new FlowPosition(new[] { 0, 1 });
        var pos2 = new FlowPosition(new[] { 0, 1, 2 });

        pos1.Should().NotBe(pos2);
    }

    #endregion

    #region Navigation Tests

    [Fact]
    public void FlowPosition_WithChild()
    {
        var position = new FlowPosition(new[] { 0 });
        var child = position.WithChild(1);

        child.Path.Should().Equal(0, 1);
    }

    [Fact]
    public void FlowPosition_MultipleWithChild()
    {
        var position = new FlowPosition(new[] { 0 });
        var child1 = position.WithChild(1);
        var child2 = child1.WithChild(2);

        child2.Path.Should().Equal(0, 1, 2);
    }

    #endregion
}
