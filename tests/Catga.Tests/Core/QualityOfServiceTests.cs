using Catga.Core;
using FluentAssertions;

namespace Catga.Tests.Core;

/// <summary>
/// Tests for QualityOfService enum
/// </summary>
public class QualityOfServiceTests
{
    #region QoS Value Tests

    [Fact]
    public void QoS_AtMostOnce_Exists()
    {
        var qos = QualityOfService.AtMostOnce;

        qos.Should().Be(QualityOfService.AtMostOnce);
    }

    [Fact]
    public void QoS_AtLeastOnce_Exists()
    {
        var qos = QualityOfService.AtLeastOnce;

        qos.Should().Be(QualityOfService.AtLeastOnce);
    }

    [Fact]
    public void QoS_ExactlyOnce_Exists()
    {
        var qos = QualityOfService.ExactlyOnce;

        qos.Should().Be(QualityOfService.ExactlyOnce);
    }

    #endregion

    #region QoS Comparison Tests

    [Fact]
    public void QoS_AllValuesUnique()
    {
        var qosValues = new[]
        {
            QualityOfService.AtMostOnce,
            QualityOfService.AtLeastOnce,
            QualityOfService.ExactlyOnce
        };

        qosValues.Distinct().Count().Should().Be(3);
    }

    [Fact]
    public void QoS_CanBeCompared()
    {
        var qos1 = QualityOfService.AtMostOnce;
        var qos2 = QualityOfService.AtLeastOnce;

        qos1.Should().NotBe(qos2);
    }

    #endregion

    #region QoS Usage Tests

    [Fact]
    public void QoS_CanBeUsedInSwitch()
    {
        var qos = QualityOfService.AtLeastOnce;
        var result = qos switch
        {
            QualityOfService.AtMostOnce => "at-most-once",
            QualityOfService.AtLeastOnce => "at-least-once",
            QualityOfService.ExactlyOnce => "exactly-once",
            _ => "unknown"
        };

        result.Should().Be("at-least-once");
    }

    [Fact]
    public void QoS_CanBeUsedInDictionary()
    {
        var dict = new Dictionary<QualityOfService, string>
        {
            [QualityOfService.AtMostOnce] = "at-most-once",
            [QualityOfService.AtLeastOnce] = "at-least-once",
            [QualityOfService.ExactlyOnce] = "exactly-once"
        };

        dict[QualityOfService.AtLeastOnce].Should().Be("at-least-once");
    }

    #endregion
}
