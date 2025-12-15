using Catga.EventSourcing;
using FluentAssertions;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Tests for ChangeType enum
/// </summary>
public class ChangeTypeTests
{
    [Fact]
    public void ChangeType_Created_ShouldExist()
    {
        ChangeType.Created.Should().Be(ChangeType.Created);
    }

    [Fact]
    public void ChangeType_Updated_ShouldExist()
    {
        ChangeType.Updated.Should().Be(ChangeType.Updated);
    }

    [Fact]
    public void ChangeType_Deleted_ShouldExist()
    {
        ChangeType.Deleted.Should().Be(ChangeType.Deleted);
    }

    [Fact]
    public void ChangeType_ShouldHaveThreeValues()
    {
        var values = Enum.GetValues<ChangeType>();
        values.Should().HaveCount(3);
    }

    [Fact]
    public void ChangeType_Values_ShouldBeDefined()
    {
        Enum.IsDefined(ChangeType.Created).Should().BeTrue();
        Enum.IsDefined(ChangeType.Updated).Should().BeTrue();
        Enum.IsDefined(ChangeType.Deleted).Should().BeTrue();
    }

    [Fact]
    public void ChangeType_Created_IntValue_ShouldBeZero()
    {
        ((int)ChangeType.Created).Should().Be(0);
    }

    [Fact]
    public void ChangeType_Updated_IntValue_ShouldBeOne()
    {
        ((int)ChangeType.Updated).Should().Be(1);
    }

    [Fact]
    public void ChangeType_Deleted_IntValue_ShouldBeTwo()
    {
        ((int)ChangeType.Deleted).Should().Be(2);
    }

    [Fact]
    public void ChangeType_CanBeParsedFromString()
    {
        Enum.Parse<ChangeType>("Created").Should().Be(ChangeType.Created);
        Enum.Parse<ChangeType>("Updated").Should().Be(ChangeType.Updated);
        Enum.Parse<ChangeType>("Deleted").Should().Be(ChangeType.Deleted);
    }

    [Fact]
    public void ChangeType_ToString_ShouldReturnName()
    {
        ChangeType.Created.ToString().Should().Be("Created");
        ChangeType.Updated.ToString().Should().Be("Updated");
        ChangeType.Deleted.ToString().Should().Be("Deleted");
    }
}
