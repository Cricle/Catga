using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow;

public class WaitTypeTests
{
    [Fact]
    public void WaitType_HasExpectedValues()
    {
        var values = Enum.GetValues<WaitType>();
        values.Should().NotBeEmpty();
    }

    [Fact]
    public void WaitType_WhenAll_Exists()
    {
        Enum.IsDefined(WaitType.WhenAll).Should().BeTrue();
    }

    [Fact]
    public void WaitType_WhenAny_Exists()
    {
        Enum.IsDefined(WaitType.WhenAny).Should().BeTrue();
    }

    [Fact]
    public void WaitType_Delay_Exists()
    {
        Enum.IsDefined(WaitType.Delay).Should().BeTrue();
    }

    [Fact]
    public void WaitType_CanBeParsedFromString()
    {
        Enum.Parse<WaitType>("WhenAll").Should().Be(WaitType.WhenAll);
        Enum.Parse<WaitType>("WhenAny").Should().Be(WaitType.WhenAny);
        Enum.Parse<WaitType>("Delay").Should().Be(WaitType.Delay);
    }

    [Fact]
    public void WaitType_ToString_ReturnsName()
    {
        WaitType.WhenAll.ToString().Should().Be("WhenAll");
        WaitType.WhenAny.ToString().Should().Be("WhenAny");
        WaitType.Delay.ToString().Should().Be("Delay");
    }
}
