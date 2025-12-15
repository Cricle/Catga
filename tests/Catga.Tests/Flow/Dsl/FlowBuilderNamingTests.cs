using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowBuilder naming conventions
/// </summary>
public class FlowBuilderNamingTests
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

    #region Flow Name Tests

    [Fact]
    public void FlowName_CanBeSet()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("MyFlow");

        builder.FlowName.Should().Be("MyFlow");
    }

    [Fact]
    public void FlowName_CanBeChanged()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("FirstName");
        builder.Name("SecondName");

        builder.FlowName.Should().Be("SecondName");
    }

    [Fact]
    public void FlowName_CanBeEmpty()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("");

        builder.FlowName.Should().BeEmpty();
    }

    [Fact]
    public void FlowName_CanBeLong()
    {
        var longName = new string('a', 500);
        var builder = new FlowBuilder<TestState>();
        builder.Name(longName);

        builder.FlowName.Should().HaveLength(500);
    }

    #endregion

    #region Naming Conventions Tests

    [Fact]
    public void FlowName_WithCamelCase_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("processOrderFlow");

        builder.FlowName.Should().Be("processOrderFlow");
    }

    [Fact]
    public void FlowName_WithPascalCase_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("ProcessOrderFlow");

        builder.FlowName.Should().Be("ProcessOrderFlow");
    }

    [Fact]
    public void FlowName_WithHyphens_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("process-order-flow");

        builder.FlowName.Should().Be("process-order-flow");
    }

    [Fact]
    public void FlowName_WithUnderscores_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("process_order_flow");

        builder.FlowName.Should().Be("process_order_flow");
    }

    #endregion

    #region Tag Naming Tests

    [Fact]
    public void Tag_CanBeSet()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd")).Tag("important");

        builder.Steps[0].Tag.Should().Be("important");
    }

    [Fact]
    public void Tag_CanBeEmpty()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd")).Tag("");

        builder.Steps[0].Tag.Should().BeEmpty();
    }

    #endregion
}
