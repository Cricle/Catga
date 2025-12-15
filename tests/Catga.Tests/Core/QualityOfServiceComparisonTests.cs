using Catga.Abstractions;
using FluentAssertions;

namespace Catga.Tests.Core;

/// <summary>
/// Tests for QualityOfService enum comparison and usage
/// </summary>
public class QualityOfServiceComparisonTests
{
    #region Enum Value Tests

    [Fact]
    public void AtMostOnce_HasValue()
    {
        var qos = QualityOfService.AtMostOnce;

        qos.Should().Be(QualityOfService.AtMostOnce);
    }

    [Fact]
    public void AtLeastOnce_HasValue()
    {
        var qos = QualityOfService.AtLeastOnce;

        qos.Should().Be(QualityOfService.AtLeastOnce);
    }

    [Fact]
    public void ExactlyOnce_HasValue()
    {
        var qos = QualityOfService.ExactlyOnce;

        qos.Should().Be(QualityOfService.ExactlyOnce);
    }

    #endregion

    #region Comparison Tests

    [Fact]
    public void AtMostOnce_NotEqualToAtLeastOnce()
    {
        (QualityOfService.AtMostOnce == QualityOfService.AtLeastOnce).Should().BeFalse();
    }

    [Fact]
    public void AtLeastOnce_NotEqualToExactlyOnce()
    {
        (QualityOfService.AtLeastOnce == QualityOfService.ExactlyOnce).Should().BeFalse();
    }

    [Fact]
    public void AllValuesUnique()
    {
        var values = new[]
        {
            QualityOfService.AtMostOnce,
            QualityOfService.AtLeastOnce,
            QualityOfService.ExactlyOnce
        };

        values.Distinct().Count().Should().Be(3);
    }

    #endregion

    #region Usage in Collections Tests

    [Fact]
    public void QoS_InDictionary_Works()
    {
        var dict = new Dictionary<QualityOfService, string>
        {
            [QualityOfService.AtMostOnce] = "Fire and forget",
            [QualityOfService.AtLeastOnce] = "Guaranteed delivery",
            [QualityOfService.ExactlyOnce] = "Idempotent"
        };

        dict[QualityOfService.AtLeastOnce].Should().Be("Guaranteed delivery");
    }

    [Fact]
    public void QoS_InList_Works()
    {
        var qosLevels = new List<QualityOfService>
        {
            QualityOfService.AtMostOnce,
            QualityOfService.AtLeastOnce,
            QualityOfService.ExactlyOnce
        };

        qosLevels.Should().HaveCount(3);
    }

    #endregion

    #region Default Value Tests

    [Fact]
    public void DefaultQoS_IsAtMostOnce()
    {
        var defaultQos = default(QualityOfService);

        defaultQos.Should().Be(QualityOfService.AtMostOnce);
    }

    #endregion
}
