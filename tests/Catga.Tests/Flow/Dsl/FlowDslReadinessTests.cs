using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Readiness tests for Flow DSL production deployment
/// </summary>
public class FlowDslReadinessTests
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

    #region Readiness Verification Tests

    [Fact]
    public void FlowDsl_CanCreateFlows()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("TestFlow");

        builder.Should().NotBeNull();
        builder.FlowName.Should().Be("TestFlow");
    }

    [Fact]
    public void FlowDsl_CanConfigureSteps()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd"));

        builder.Steps.Should().HaveCount(1);
    }

    [Fact]
    public void FlowDsl_CanConfigureTimeouts()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Timeout(TimeSpan.FromSeconds(30)).ForTag("api");

        builder.TaggedTimeouts.Should().ContainKey("api");
    }

    [Fact]
    public void FlowDsl_CanConfigureRetries()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Retry(3).ForTag("api");

        builder.TaggedRetries.Should().ContainKey("api");
    }

    [Fact]
    public void FlowDsl_CanConfigurePersistence()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Persist().ForTag("checkpoint");

        builder.TaggedPersist.Should().Contain("checkpoint");
    }

    [Fact]
    public void FlowDsl_CanConfigureCallbacks()
    {
        var builder = new FlowBuilder<TestState>();
        builder.OnFlowCompleted(s => new TestCommand("completed"));

        builder.OnFlowCompletedFactory.Should().NotBeNull();
    }

    #endregion

    #region Production Readiness Tests

    [Fact]
    public void FlowDsl_ProductionReady()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Name("ProductionFlow")
            .Timeout(TimeSpan.FromSeconds(60)).ForTag("critical")
            .Retry(5).ForTag("critical")
            .Persist().ForTag("checkpoint")
            .Send(s => new TestCommand("1")).Tag("critical")
            .If(s => true)
                .Send(s => new TestCommand("2"))
            .EndIf()
            .OnFlowCompleted(s => new TestCommand("completed"))
            .OnFlowFailed((s, error) => new TestCommand($"failed: {error}"));

        builder.FlowName.Should().Be("ProductionFlow");
        builder.Steps.Should().HaveCount(2);
        builder.TaggedTimeouts.Should().NotBeEmpty();
        builder.TaggedRetries.Should().NotBeEmpty();
        builder.TaggedPersist.Should().NotBeEmpty();
        builder.OnFlowCompletedFactory.Should().NotBeNull();
        builder.OnFlowFailedFactory.Should().NotBeNull();
    }

    #endregion
}
