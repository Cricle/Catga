using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

public class QueryBuilderTests
{
    private class TestState : BaseFlowState
    {
        public string Data { get; set; } = "";
        public string? QueryResult { get; set; }
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestQuery(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    [Fact]
    public void Query_CreatesQueryStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Query(s => new TestQuery(s.Data));

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.Query);
    }

    [Fact]
    public void Query_WithTag_SetsTag()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Query(s => new TestQuery(s.Data)).Tag("query-tag");

        builder.Steps[0].Tag.Should().Be("query-tag");
    }

    [Fact]
    public void Query_WithInto_SetsResultSetter()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Query(s => new TestQuery(s.Data))
            .Into((s, result) => s.QueryResult = result?.ToString());

        builder.Steps[0].ResultSetter.Should().NotBeNull();
    }

    [Fact]
    public void Query_ChainedWithSend_AddsMultipleSteps()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Query(s => new TestQuery("query1"))
            .Query(s => new TestQuery("query2"));

        builder.Steps.Should().HaveCount(2);
        builder.Steps.All(s => s.Type == StepType.Query).Should().BeTrue();
    }
}
