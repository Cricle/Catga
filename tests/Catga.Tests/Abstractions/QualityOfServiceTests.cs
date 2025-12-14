using Catga.Abstractions;
using FluentAssertions;

namespace Catga.Tests.Abstractions;

public class QualityOfServiceTests
{
    [Fact]
    public void QualityOfService_HasExpectedValues()
    {
        Enum.GetValues<QualityOfService>().Should().HaveCount(3);
    }

    [Theory]
    [InlineData(QualityOfService.AtMostOnce, 0)]
    [InlineData(QualityOfService.AtLeastOnce, 1)]
    [InlineData(QualityOfService.ExactlyOnce, 2)]
    public void QualityOfService_HasExpectedIntValues(QualityOfService qos, int expected)
    {
        ((int)qos).Should().Be(expected);
    }

    [Fact]
    public void QualityOfService_CanBeParsedFromString()
    {
        Enum.Parse<QualityOfService>("AtMostOnce").Should().Be(QualityOfService.AtMostOnce);
        Enum.Parse<QualityOfService>("AtLeastOnce").Should().Be(QualityOfService.AtLeastOnce);
        Enum.Parse<QualityOfService>("ExactlyOnce").Should().Be(QualityOfService.ExactlyOnce);
    }

    [Fact]
    public void QualityOfService_ToString_ReturnsName()
    {
        QualityOfService.AtMostOnce.ToString().Should().Be("AtMostOnce");
        QualityOfService.AtLeastOnce.ToString().Should().Be("AtLeastOnce");
        QualityOfService.ExactlyOnce.ToString().Should().Be("ExactlyOnce");
    }

    [Fact]
    public void QualityOfService_IsDefined_ReturnsTrue()
    {
        Enum.IsDefined(QualityOfService.AtMostOnce).Should().BeTrue();
        Enum.IsDefined(QualityOfService.AtLeastOnce).Should().BeTrue();
        Enum.IsDefined(QualityOfService.ExactlyOnce).Should().BeTrue();
    }

    [Fact]
    public void QualityOfService_InvalidValue_IsNotDefined()
    {
        Enum.IsDefined((QualityOfService)999).Should().BeFalse();
    }
}
