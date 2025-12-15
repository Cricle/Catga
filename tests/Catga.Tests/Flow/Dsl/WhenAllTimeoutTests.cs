using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for WhenAll/WhenAny timeout configuration
/// </summary>
public class WhenAllTimeoutTests
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

    #region WhenAll Timeout Tests

    [Fact]
    public void WhenAll_WithTimeout_SetsTimeout()
    {
        var builder = new FlowBuilder<TestState>();
        builder.WhenAll(
            b => b.Send(s => new TestCommand("1")),
            b => b.Send(s => new TestCommand("2"))
        ).Timeout(TimeSpan.FromSeconds(30));

        builder.Steps[0].Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(300)]
    public void WhenAll_VariousTimeouts_AllWork(int seconds)
    {
        var builder = new FlowBuilder<TestState>();
        builder.WhenAll(
            b => b.Send(s => new TestCommand("1"))
        ).Timeout(TimeSpan.FromSeconds(seconds));

        builder.Steps[0].Timeout!.Value.TotalSeconds.Should().Be(seconds);
    }

    #endregion

    #region WhenAny Timeout Tests

    [Fact]
    public void WhenAny_WithTimeout_SetsTimeout()
    {
        var builder = new FlowBuilder<TestState>();
        builder.WhenAny(
            b => b.Send(s => new TestCommand("1")),
            b => b.Send(s => new TestCommand("2"))
        ).Timeout(TimeSpan.FromMinutes(1));

        builder.Steps[0].Timeout.Should().Be(TimeSpan.FromMinutes(1));
    }

    #endregion

    #region WhenAll with IfAnyFail Tests

    [Fact]
    public void WhenAll_WithIfAnyFail_Continue_SetsFlag()
    {
        var builder = new FlowBuilder<TestState>();
        builder.WhenAll(
            b => b.Send(s => new TestCommand("1")),
            b => b.Send(s => new TestCommand("2"))
        ).IfAnyFail().Continue();

        builder.Steps[0].ContinueOnFailure.Should().BeTrue();
    }

    [Fact]
    public void WhenAll_WithIfAnyFail_Stop_SetsFlag()
    {
        var builder = new FlowBuilder<TestState>();
        builder.WhenAll(
            b => b.Send(s => new TestCommand("1")),
            b => b.Send(s => new TestCommand("2"))
        ).IfAnyFail().Stop();

        builder.Steps[0].ContinueOnFailure.Should().BeFalse();
    }

    #endregion

    #region Combined Configuration Tests

    [Fact]
    public void WhenAll_TimeoutAndIfAnyFail_BothApplied()
    {
        var builder = new FlowBuilder<TestState>();
        builder.WhenAll(
            b => b.Send(s => new TestCommand("1")),
            b => b.Send(s => new TestCommand("2"))
        )
        .Timeout(TimeSpan.FromSeconds(60))
        .IfAnyFail().Continue();

        var step = builder.Steps[0];
        step.Timeout.Should().Be(TimeSpan.FromSeconds(60));
        step.ContinueOnFailure.Should().BeTrue();
    }

    #endregion
}
