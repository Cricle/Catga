using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for StepType enum
/// </summary>
public class StepTypeEnumTests
{
    [Fact]
    public void StepType_Send_IsDefined()
    {
        Enum.IsDefined(typeof(StepType), StepType.Send).Should().BeTrue();
    }

    [Fact]
    public void StepType_Query_IsDefined()
    {
        Enum.IsDefined(typeof(StepType), StepType.Query).Should().BeTrue();
    }

    [Fact]
    public void StepType_Publish_IsDefined()
    {
        Enum.IsDefined(typeof(StepType), StepType.Publish).Should().BeTrue();
    }

    [Fact]
    public void StepType_If_IsDefined()
    {
        Enum.IsDefined(typeof(StepType), StepType.If).Should().BeTrue();
    }

    [Fact]
    public void StepType_Switch_IsDefined()
    {
        Enum.IsDefined(typeof(StepType), StepType.Switch).Should().BeTrue();
    }

    [Fact]
    public void StepType_ForEach_IsDefined()
    {
        Enum.IsDefined(typeof(StepType), StepType.ForEach).Should().BeTrue();
    }

    [Fact]
    public void StepType_WhenAll_IsDefined()
    {
        Enum.IsDefined(typeof(StepType), StepType.WhenAll).Should().BeTrue();
    }

    [Fact]
    public void StepType_WhenAny_IsDefined()
    {
        Enum.IsDefined(typeof(StepType), StepType.WhenAny).Should().BeTrue();
    }

    [Fact]
    public void StepType_Delay_IsDefined()
    {
        Enum.IsDefined(typeof(StepType), StepType.Delay).Should().BeTrue();
    }

    [Fact]
    public void StepType_Wait_IsDefined()
    {
        Enum.IsDefined(typeof(StepType), StepType.Wait).Should().BeTrue();
    }

    [Fact]
    public void StepType_AllValues_AreUnique()
    {
        var values = Enum.GetValues<StepType>().Cast<int>().ToList();
        values.Distinct().Count().Should().Be(values.Count);
    }

    [Fact]
    public void StepType_CanBeUsedInSwitch()
    {
        var stepType = StepType.Send;

        var result = stepType switch
        {
            StepType.Send => "send",
            StepType.Query => "query",
            StepType.Publish => "publish",
            StepType.If => "if",
            StepType.Switch => "switch",
            StepType.ForEach => "foreach",
            StepType.WhenAll => "whenall",
            StepType.WhenAny => "whenany",
            StepType.Delay => "delay",
            StepType.Wait => "wait",
            _ => "unknown"
        };

        result.Should().Be("send");
    }

    [Fact]
    public void StepType_CanBeUsedInDictionary()
    {
        var dict = new Dictionary<StepType, string>
        {
            [StepType.Send] = "Send Command",
            [StepType.Query] = "Query Data",
            [StepType.Publish] = "Publish Event"
        };

        dict[StepType.Send].Should().Be("Send Command");
    }
}
