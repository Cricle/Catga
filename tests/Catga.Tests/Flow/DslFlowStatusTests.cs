using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow;

public class DslFlowStatusTests
{
    [Fact]
    public void DslFlowStatus_HasExpectedValues()
    {
        Enum.GetValues<DslFlowStatus>().Should().Contain(DslFlowStatus.Pending);
        Enum.GetValues<DslFlowStatus>().Should().Contain(DslFlowStatus.Running);
        Enum.GetValues<DslFlowStatus>().Should().Contain(DslFlowStatus.WaitingForResponse);
        Enum.GetValues<DslFlowStatus>().Should().Contain(DslFlowStatus.Completed);
        Enum.GetValues<DslFlowStatus>().Should().Contain(DslFlowStatus.Failed);
        Enum.GetValues<DslFlowStatus>().Should().Contain(DslFlowStatus.Cancelled);
    }

    [Theory]
    [InlineData(DslFlowStatus.Pending, 0)]
    [InlineData(DslFlowStatus.Running, 1)]
    [InlineData(DslFlowStatus.WaitingForResponse, 2)]
    [InlineData(DslFlowStatus.Completed, 3)]
    [InlineData(DslFlowStatus.Failed, 4)]
    [InlineData(DslFlowStatus.Cancelled, 5)]
    public void DslFlowStatus_HasExpectedIntValues(DslFlowStatus status, int expected)
    {
        ((int)status).Should().Be(expected);
    }

    [Fact]
    public void DslFlowStatus_CanBeParsedFromString()
    {
        Enum.Parse<DslFlowStatus>("Running").Should().Be(DslFlowStatus.Running);
        Enum.Parse<DslFlowStatus>("Completed").Should().Be(DslFlowStatus.Completed);
        Enum.Parse<DslFlowStatus>("Failed").Should().Be(DslFlowStatus.Failed);
    }

    [Fact]
    public void DslFlowStatus_ToString_ReturnsName()
    {
        DslFlowStatus.Running.ToString().Should().Be("Running");
        DslFlowStatus.Completed.ToString().Should().Be("Completed");
        DslFlowStatus.Failed.ToString().Should().Be("Failed");
    }
}
