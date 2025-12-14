using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow;

public class StepTypeTests
{
    [Fact]
    public void StepType_HasExpectedValues()
    {
        var values = Enum.GetValues<StepType>();
        values.Should().NotBeEmpty();
    }

    [Fact]
    public void StepType_Send_Exists()
    {
        Enum.IsDefined(StepType.Send).Should().BeTrue();
    }

    [Fact]
    public void StepType_Query_Exists()
    {
        Enum.IsDefined(StepType.Query).Should().BeTrue();
    }

    [Fact]
    public void StepType_Publish_Exists()
    {
        Enum.IsDefined(StepType.Publish).Should().BeTrue();
    }

    [Fact]
    public void StepType_If_Exists()
    {
        Enum.IsDefined(StepType.If).Should().BeTrue();
    }

    [Fact]
    public void StepType_Switch_Exists()
    {
        Enum.IsDefined(StepType.Switch).Should().BeTrue();
    }

    [Fact]
    public void StepType_ForEach_Exists()
    {
        Enum.IsDefined(StepType.ForEach).Should().BeTrue();
    }

    [Fact]
    public void StepType_CanBeParsedFromString()
    {
        Enum.Parse<StepType>("Send").Should().Be(StepType.Send);
        Enum.Parse<StepType>("Query").Should().Be(StepType.Query);
        Enum.Parse<StepType>("Publish").Should().Be(StepType.Publish);
    }

    [Fact]
    public void StepType_ToString_ReturnsName()
    {
        StepType.Send.ToString().Should().Be("Send");
        StepType.Query.ToString().Should().Be("Query");
        StepType.Publish.ToString().Should().Be("Publish");
    }
}
