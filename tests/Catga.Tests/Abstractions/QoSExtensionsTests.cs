using Catga.Abstractions;
using FluentAssertions;

namespace Catga.Tests.Abstractions;

public class QoSExtensionsTests
{
    [Theory]
    [InlineData(QualityOfService.AtMostOnce, "AtMostOnce")]
    [InlineData(QualityOfService.AtLeastOnce, "AtLeastOnce")]
    [InlineData(QualityOfService.ExactlyOnce, "ExactlyOnce")]
    public void ToTagString_ReturnsCorrectString(QualityOfService qos, string expected)
    {
        qos.ToTagString().Should().Be(expected);
    }

    [Fact]
    public void ToTagString_UnknownValue_ReturnsUnknown()
    {
        var unknownQoS = (QualityOfService)999;
        unknownQoS.ToTagString().Should().Be("Unknown");
    }

    [Fact]
    public void RequiresAck_AtMostOnce_ReturnsFalse()
    {
        QualityOfService.AtMostOnce.RequiresAck().Should().BeFalse();
    }

    [Fact]
    public void RequiresAck_AtLeastOnce_ReturnsTrue()
    {
        QualityOfService.AtLeastOnce.RequiresAck().Should().BeTrue();
    }

    [Fact]
    public void RequiresAck_ExactlyOnce_ReturnsTrue()
    {
        QualityOfService.ExactlyOnce.RequiresAck().Should().BeTrue();
    }

    [Fact]
    public void RequiresDedup_AtMostOnce_ReturnsFalse()
    {
        QualityOfService.AtMostOnce.RequiresDedup().Should().BeFalse();
    }

    [Fact]
    public void RequiresDedup_AtLeastOnce_ReturnsFalse()
    {
        QualityOfService.AtLeastOnce.RequiresDedup().Should().BeFalse();
    }

    [Fact]
    public void RequiresDedup_ExactlyOnce_ReturnsTrue()
    {
        QualityOfService.ExactlyOnce.RequiresDedup().Should().BeTrue();
    }

    [Theory]
    [InlineData(QualityOfService.AtMostOnce, false, false)]
    [InlineData(QualityOfService.AtLeastOnce, true, false)]
    [InlineData(QualityOfService.ExactlyOnce, true, true)]
    public void QoS_Combinations_AreCorrect(QualityOfService qos, bool requiresAck, bool requiresDedup)
    {
        qos.RequiresAck().Should().Be(requiresAck);
        qos.RequiresDedup().Should().Be(requiresDedup);
    }

    [Fact]
    public void AllQoSValues_HaveValidTagStrings()
    {
        foreach (var qos in Enum.GetValues<QualityOfService>())
        {
            var tagString = qos.ToTagString();
            tagString.Should().NotBeNullOrEmpty();
            tagString.Should().NotBe("Unknown");
        }
    }

    [Fact]
    public void ToTagString_IsConsistent()
    {
        var qos = QualityOfService.AtLeastOnce;

        var result1 = qos.ToTagString();
        var result2 = qos.ToTagString();

        result1.Should().Be(result2);
    }

    [Fact]
    public void ToTagString_DoesNotAllocateNewString_ForKnownValues()
    {
        var qos = QualityOfService.AtLeastOnce;

        var result1 = qos.ToTagString();
        var result2 = qos.ToTagString();

        ReferenceEquals(result1, result2).Should().BeTrue();
    }
}
