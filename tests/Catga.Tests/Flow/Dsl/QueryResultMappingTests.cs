using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for Query result mapping with Into()
/// </summary>
public class QueryResultMappingTests
{
    private class TestState : BaseFlowState
    {
        public string Id { get; set; } = "";
        public string Result { get; set; } = "";
        public int Count { get; set; }
        public List<string> Items { get; set; } = new();
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record GetDataQuery(string Id) : IRequest<string>
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    private record GetCountQuery(string Id) : IRequest<int>
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    private record GetItemsQuery(string Id) : IRequest<List<string>>
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    #region Basic Query Tests

    [Fact]
    public void Query_CreatesQueryStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Query(s => new GetDataQuery(s.Id));

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.Query);
    }

    [Fact]
    public void Query_WithInto_SetsResultSetter()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Query(s => new GetDataQuery(s.Id))
            .Into(s => s.Result);

        builder.Steps[0].ResultSetter.Should().NotBeNull();
    }

    #endregion

    #region Into Mapping Tests

    [Fact]
    public void Into_StringProperty_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Query(s => new GetDataQuery(s.Id))
            .Into(s => s.Result);

        builder.Steps[0].ResultSetter.Should().NotBeNull();
    }

    [Fact]
    public void Into_IntProperty_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Query(s => new GetCountQuery(s.Id))
            .Into(s => s.Count);

        builder.Steps[0].ResultSetter.Should().NotBeNull();
    }

    [Fact]
    public void Into_ListProperty_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Query(s => new GetItemsQuery(s.Id))
            .Into(s => s.Items);

        builder.Steps[0].ResultSetter.Should().NotBeNull();
    }

    #endregion

    #region Query Chaining Tests

    [Fact]
    public void Query_ChainedWithTag_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Query(s => new GetDataQuery(s.Id))
            .Into(s => s.Result)
            .Tag("data-fetch");

        builder.Steps[0].Tag.Should().Be("data-fetch");
    }

    [Fact]
    public void MultipleQueries_AllCreated()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Query(s => new GetDataQuery(s.Id)).Into(s => s.Result)
            .Query(s => new GetCountQuery(s.Id)).Into(s => s.Count);

        builder.Steps.Should().HaveCount(2);
        builder.Steps.All(s => s.Type == StepType.Query).Should().BeTrue();
    }

    #endregion
}
