using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowBuilder fluent return types
/// </summary>
public class FlowBuilderReturnTypeTests
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

    #region FlowBuilder Return Tests

    [Fact]
    public void Name_ReturnsFlowBuilder()
    {
        var builder = new FlowBuilder<TestState>();
        var result = builder.Name("test");

        result.Should().BeOfType<FlowBuilder<TestState>>();
    }

    [Fact]
    public void Timeout_ReturnsTaggedSetting()
    {
        var builder = new FlowBuilder<TestState>();
        var result = builder.Timeout(TimeSpan.FromSeconds(30));

        result.Should().NotBeNull();
    }

    [Fact]
    public void Retry_ReturnsTaggedSetting()
    {
        var builder = new FlowBuilder<TestState>();
        var result = builder.Retry(3);

        result.Should().NotBeNull();
    }

    [Fact]
    public void Persist_ReturnsTaggedSetting()
    {
        var builder = new FlowBuilder<TestState>();
        var result = builder.Persist();

        result.Should().NotBeNull();
    }

    #endregion

    #region Fluent Chaining Tests

    [Fact]
    public void ChainedCalls_AllWork()
    {
        var builder = new FlowBuilder<TestState>();
        var result = builder
            .Name("test")
            .Send(s => new TestCommand("1"))
            .Send(s => new TestCommand("2"));

        result.Should().BeOfType<FlowBuilder<TestState>>();
        result.Steps.Should().HaveCount(2);
    }

    [Fact]
    public void ComplexChaining_Works()
    {
        var builder = new FlowBuilder<TestState>();
        var result = builder
            .Name("complex")
            .Timeout(TimeSpan.FromSeconds(30)).ForTag("api")
            .Send(s => new TestCommand("1")).Tag("api")
            .If(s => true)
                .Send(s => new TestCommand("2"))
            .EndIf();

        result.Steps.Should().HaveCount(2);
    }

    #endregion
}
