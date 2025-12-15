using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowBuilder usage patterns and best practices
/// </summary>
public class FlowBuilderUsagePatternTests
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

    #region Usage Pattern Tests

    [Fact]
    public void Pattern_FluentChaining()
    {
        var builder = new FlowBuilder<TestState>()
            .Name("FluentFlow")
            .Send(s => new TestCommand("1"))
            .Send(s => new TestCommand("2"))
            .Send(s => new TestCommand("3"));

        builder.FlowName.Should().Be("FluentFlow");
        builder.Steps.Should().HaveCount(3);
    }

    [Fact]
    public void Pattern_ConfigurationFirst()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Name("ConfigFirst")
            .Timeout(TimeSpan.FromSeconds(30)).ForTag("api")
            .Retry(3).ForTag("api")
            .Persist().ForTag("checkpoint");

        builder.Send(s => new TestCommand("1")).Tag("api");
        builder.Send(s => new TestCommand("2")).Tag("checkpoint");

        builder.TaggedTimeouts.Should().ContainKey("api");
        builder.TaggedRetries.Should().ContainKey("api");
        builder.TaggedPersist.Should().Contain("checkpoint");
    }

    [Fact]
    public void Pattern_NestedBranches()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .If(s => true)
                .If(s => true)
                    .Send(s => new TestCommand("nested"))
                .EndIf()
            .EndIf();

        builder.Steps.Should().HaveCount(1);
    }

    [Fact]
    public void Pattern_MixedStepTypes()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("send"))
            .Query(s => new TestCommand("query"))
            .Publish(s => new TestCommand("publish"))
            .Delay(TimeSpan.FromSeconds(1))
            .Wait(new WaitCondition("corr", "Event"));

        builder.Steps.Should().HaveCount(5);
    }

    #endregion

    #region Best Practice Tests

    [Fact]
    public void BestPractice_TagCriticalSteps()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Timeout(TimeSpan.FromSeconds(60)).ForTag("critical")
            .Retry(5).ForTag("critical")
            .Send(s => new TestCommand("1")).Tag("critical")
            .Send(s => new TestCommand("2")).Tag("critical");

        builder.Steps.All(s => s.Tag == "critical").Should().BeTrue();
    }

    [Fact]
    public void BestPractice_UseOptionalForNonCritical()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("critical"))
            .Send(s => new TestCommand("optional")).Optional()
            .Send(s => new TestCommand("critical2"));

        builder.Steps[0].IsOptional.Should().BeFalse();
        builder.Steps[1].IsOptional.Should().BeTrue();
        builder.Steps[2].IsOptional.Should().BeFalse();
    }

    #endregion
}
