using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Catga.Tests.Flow.Dsl;

public class DslFlowExecutorAdditionalTests
{
    private class TestFlowState : BaseFlowState
    {
        public string OrderId { get; set; } = "";
        public decimal Amount { get; set; }
        public bool IsProcessed { get; set; }
        public string? Result { get; set; }

        public override IEnumerable<string> GetChangedFieldNames()
        {
            yield break;
        }
    }

    [Fact]
    public void DslFlowExecutor_CanBeCreated()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IMessageTransport>());
        var sp = services.BuildServiceProvider();

        var executor = new DslFlowExecutor<TestFlowState>(sp);

        executor.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyFlow_CompletesSuccessfully()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IMessageTransport>());
        var sp = services.BuildServiceProvider();

        var executor = new DslFlowExecutor<TestFlowState>(sp);
        var state = new TestFlowState { FlowId = "test-1", OrderId = "ORD-001" };
        var steps = new List<FlowStep>();

        var result = await executor.ExecuteAsync(state, steps);

        result.Status.Should().Be(DslFlowStatus.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_WithState_PreservesFlowId()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IMessageTransport>());
        var sp = services.BuildServiceProvider();

        var executor = new DslFlowExecutor<TestFlowState>(sp);
        var state = new TestFlowState { FlowId = "flow-preserve-test", OrderId = "ORD-002" };
        var steps = new List<FlowStep>();

        var result = await executor.ExecuteAsync(state, steps);

        result.State.FlowId.Should().Be("flow-preserve-test");
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellation_RespectsToken()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IMessageTransport>());
        var sp = services.BuildServiceProvider();

        var executor = new DslFlowExecutor<TestFlowState>(sp);
        var state = new TestFlowState { FlowId = "cancel-test" };
        var steps = new List<FlowStep>();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => executor.ExecuteAsync(state, steps, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void FlowStep_Send_CanBeConfigured()
    {
        var step = new FlowStep
        {
            Type = StepType.Send,
            Tag = "send-order"
        };

        step.Type.Should().Be(StepType.Send);
        step.Tag.Should().Be("send-order");
    }

    [Fact]
    public void FlowStep_Query_CanBeConfigured()
    {
        var step = new FlowStep
        {
            Type = StepType.Query,
            Tag = "query-status"
        };

        step.Type.Should().Be(StepType.Query);
        step.Tag.Should().Be("query-status");
    }

    [Fact]
    public void FlowStep_Publish_CanBeConfigured()
    {
        var step = new FlowStep
        {
            Type = StepType.Publish,
            Tag = "publish-event"
        };

        step.Type.Should().Be(StepType.Publish);
        step.Tag.Should().Be("publish-event");
    }
}
