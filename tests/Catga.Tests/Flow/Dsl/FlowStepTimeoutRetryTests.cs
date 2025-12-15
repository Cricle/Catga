using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowStep timeout and retry configuration
/// </summary>
public class FlowStepTimeoutRetryTests
{
    private class TestState : BaseFlowState
    {
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    #region Timeout Tests

    [Fact]
    public void Step_CanHaveTimeout()
    {
        var step = new FlowStep { Timeout = TimeSpan.FromSeconds(30) };

        step.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Step_TimeoutCanBeChanged()
    {
        var step = new FlowStep { Timeout = TimeSpan.FromSeconds(30) };
        step.Timeout = TimeSpan.FromSeconds(60);

        step.Timeout.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void Step_TimeoutCanBeZero()
    {
        var step = new FlowStep { Timeout = TimeSpan.Zero };

        step.Timeout.Should().Be(TimeSpan.Zero);
    }

    #endregion

    #region Retry Tests

    [Fact]
    public void Step_CanHaveRetry()
    {
        var step = new FlowStep { Retry = 3 };

        step.Retry.Should().Be(3);
    }

    [Fact]
    public void Step_RetryCanBeChanged()
    {
        var step = new FlowStep { Retry = 3 };
        step.Retry = 5;

        step.Retry.Should().Be(5);
    }

    [Fact]
    public void Step_RetryCanBeZero()
    {
        var step = new FlowStep { Retry = 0 };

        step.Retry.Should().Be(0);
    }

    [Fact]
    public void Step_RetryCanBeNegative()
    {
        var step = new FlowStep { Retry = -1 };

        step.Retry.Should().Be(-1);
    }

    #endregion

    #region Combined Timeout and Retry Tests

    [Fact]
    public void Step_CanHaveBothTimeoutAndRetry()
    {
        var step = new FlowStep
        {
            Timeout = TimeSpan.FromSeconds(30),
            Retry = 3
        };

        step.Timeout.Should().Be(TimeSpan.FromSeconds(30));
        step.Retry.Should().Be(3);
    }

    #endregion
}
