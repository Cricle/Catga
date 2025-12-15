using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Summary tests covering all major FlowBuilder capabilities
/// </summary>
public class FlowBuilderSummaryTests
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

    #region Summary of All Features

    [Fact]
    public void FlowBuilder_Summary_AllFeaturesWork()
    {
        // Arrange
        var builder = new FlowBuilder<TestState>();

        // Act - Configure flow
        builder.Name("SummaryFlow");
        builder.Timeout(TimeSpan.FromSeconds(30)).ForTag("default");
        builder.Retry(3).ForTag("default");
        builder.Persist().ForTag("checkpoint");

        // Act - Add steps
        builder.Send(s => new TestCommand("1")).Tag("default");
        builder.Query(s => new TestCommand("2")).Tag("default");
        builder.Publish(s => new TestCommand("3"));
        builder.Delay(TimeSpan.FromSeconds(1));
        builder.Wait(new WaitCondition("corr-1", "Event"));

        // Assert
        builder.FlowName.Should().Be("SummaryFlow");
        builder.Steps.Should().HaveCount(5);
        builder.DefaultTimeout.Should().Be(TimeSpan.FromMinutes(10));
        builder.DefaultRetries.Should().Be(0);
        builder.TaggedTimeouts.Should().ContainKey("default");
        builder.TaggedRetries.Should().ContainKey("default");
        builder.TaggedPersist.Should().Contain("checkpoint");
    }

    #endregion

    #region Integration Summary

    [Fact]
    public void FlowBuilder_Integration_AllComponentsWork()
    {
        var builder = new FlowBuilder<TestState>();

        // Basic configuration
        builder.Name("IntegrationFlow");

        // Branching
        builder.If(s => true)
            .Send(s => new TestCommand("if-branch"))
        .EndIf();

        // Looping
        builder.ForEach(s => new[] { "a", "b" })
            .Send((s, item) => new TestCommand(item))
        .EndForEach();

        // Switching
        builder.Switch(s => 1)
            .Case(1, c => c.Send(s => new TestCommand("case-1")))
        .EndSwitch();

        // Parallel
        builder.WhenAll(
            b => b.Send(s => new TestCommand("parallel-1")),
            b => b.Send(s => new TestCommand("parallel-2"))
        );

        builder.Steps.Should().HaveCount(5);
    }

    #endregion

    #region Verification Tests

    [Fact]
    public void FlowBuilder_VerifyAllPropertiesSet()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("VerifyFlow");

        builder.FlowName.Should().NotBeNullOrEmpty();
        builder.Steps.Should().NotBeNull();
        builder.DefaultTimeout.Should().NotBe(TimeSpan.Zero);
        builder.DefaultRetries.Should().BeGreaterThanOrEqualTo(0);
        builder.TaggedTimeouts.Should().NotBeNull();
        builder.TaggedRetries.Should().NotBeNull();
        builder.TaggedPersist.Should().NotBeNull();
    }

    #endregion
}
