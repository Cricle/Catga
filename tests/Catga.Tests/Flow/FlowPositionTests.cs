using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow;

public class FlowPositionTests
{
    [Fact]
    public void Constructor_WithEmptyPath_CreatesValidPosition()
    {
        var position = new FlowPosition(Array.Empty<int>());

        position.Path.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithSingleElement_CreatesValidPosition()
    {
        var position = new FlowPosition(new[] { 5 });

        position.Path.Should().BeEquivalentTo(new[] { 5 });
    }

    [Fact]
    public void Constructor_WithMultipleElements_CreatesValidPosition()
    {
        var position = new FlowPosition(new[] { 1, 2, 3, 4 });

        position.Path.Should().BeEquivalentTo(new[] { 1, 2, 3, 4 });
    }

    [Fact]
    public void Equality_SamePath_AreEqual()
    {
        var pos1 = new FlowPosition(new[] { 1, 2, 3 });
        var pos2 = new FlowPosition(new[] { 1, 2, 3 });

        pos1.Should().Be(pos2);
    }

    [Fact]
    public void Equality_DifferentPath_AreNotEqual()
    {
        var pos1 = new FlowPosition(new[] { 1, 2, 3 });
        var pos2 = new FlowPosition(new[] { 1, 2, 4 });

        pos1.Should().NotBe(pos2);
    }

    [Fact]
    public void Equality_DifferentLength_AreNotEqual()
    {
        var pos1 = new FlowPosition(new[] { 1, 2 });
        var pos2 = new FlowPosition(new[] { 1, 2, 3 });

        pos1.Should().NotBe(pos2);
    }

    [Fact]
    public void Path_IsImmutable()
    {
        var original = new[] { 1, 2, 3 };
        var position = new FlowPosition(original);

        original[0] = 999;

        position.Path[0].Should().Be(1);
    }

    [Theory]
    [InlineData(new int[] { }, new int[] { })]
    [InlineData(new int[] { 0 }, new int[] { 0 })]
    [InlineData(new int[] { 1, 2, 3 }, new int[] { 1, 2, 3 })]
    public void Constructor_PreservesPath(int[] input, int[] expected)
    {
        var position = new FlowPosition(input);

        position.Path.Should().BeEquivalentTo(expected);
    }
}
