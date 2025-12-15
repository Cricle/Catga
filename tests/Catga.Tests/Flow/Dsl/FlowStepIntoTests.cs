using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowStep result mapping with Into()
/// </summary>
public class FlowStepIntoTests
{
    private class TestState : BaseFlowState
    {
        public string Result { get; set; } = "";
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestQuery(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    #region Into() Configuration Tests

    [Fact]
    public void QueryStep_CanMapResultWithInto()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Query(s => new TestQuery("q1")).Into(s => s.Result);

        var queryStep = builder.Steps[0];
        queryStep.Type.Should().Be(StepType.Query);
        queryStep.ResultSetter.Should().NotBeNull();
    }

    [Fact]
    public void MultipleQueries_CanHaveDifferentIntoMappings()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Query(s => new TestQuery("q1")).Into(s => s.Result)
            .Query(s => new TestQuery("q2")).Into(s => s.Result);

        builder.Steps[0].ResultSetter.Should().NotBeNull();
        builder.Steps[1].ResultSetter.Should().NotBeNull();
    }

    #endregion

    #region Into() with Tag Tests

    [Fact]
    public void IntoStep_CanHaveTag()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Query(s => new TestQuery("q1")).Into(s => s.Result).Tag("query-api");

        var step = builder.Steps[0];
        step.ResultSetter.Should().NotBeNull();
        step.Tag.Should().Be("query-api");
    }

    #endregion
}
