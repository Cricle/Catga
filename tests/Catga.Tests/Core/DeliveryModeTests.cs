using Catga.Core;
using FluentAssertions;

namespace Catga.Tests.Core;

public class DeliveryModeTests
{
    [Fact]
    public void DeliveryMode_HasExpectedValues()
    {
        var values = Enum.GetValues<DeliveryMode>();
        values.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(DeliveryMode.WaitForResult, 0)]
    [InlineData(DeliveryMode.AsyncRetry, 1)]
    public void DeliveryMode_HasExpectedIntValues(DeliveryMode mode, int expected)
    {
        ((int)mode).Should().Be(expected);
    }

    [Fact]
    public void DeliveryMode_CanBeParsedFromString()
    {
        Enum.Parse<DeliveryMode>("WaitForResult").Should().Be(DeliveryMode.WaitForResult);
        Enum.Parse<DeliveryMode>("AsyncRetry").Should().Be(DeliveryMode.AsyncRetry);
    }

    [Fact]
    public void DeliveryMode_ToString_ReturnsName()
    {
        DeliveryMode.WaitForResult.ToString().Should().Be("WaitForResult");
        DeliveryMode.AsyncRetry.ToString().Should().Be("AsyncRetry");
    }

    [Fact]
    public void DeliveryMode_IsDefined_ReturnsTrue()
    {
        Enum.IsDefined(DeliveryMode.WaitForResult).Should().BeTrue();
        Enum.IsDefined(DeliveryMode.AsyncRetry).Should().BeTrue();
    }

    [Fact]
    public void DeliveryMode_InvalidValue_IsNotDefined()
    {
        Enum.IsDefined((DeliveryMode)999).Should().BeFalse();
    }
}
