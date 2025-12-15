using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Basic tests for DslFlowExecutor
/// </summary>
public class FlowExecutorBasicTests
{
    private class TestState : BaseFlowState
    {
        public int Value { get; set; }
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    #region Executor Creation Tests

    [Fact]
    public void DslFlowExecutor_CanBeCreated()
    {
        var executor = new DslFlowExecutor();

        executor.Should().NotBeNull();
    }

    #endregion

    #region Flow Execution Tests

    [Fact]
    public void ExecuteAsync_WithEmptyFlow_Completes()
    {
        var executor = new DslFlowExecutor();
        var config = new TestFlowConfig();
        config.Build();

        var state = new TestState();
        var result = executor.ExecuteAsync(config, state, CancellationToken.None);

        result.Should().NotBeNull();
    }

    #endregion

    private class TestFlowConfig : FlowConfig<TestState>
    {
        protected override void Configure(IFlowBuilder<TestState> flow)
        {
            flow.Name("TestFlow");
        }
    }
}
