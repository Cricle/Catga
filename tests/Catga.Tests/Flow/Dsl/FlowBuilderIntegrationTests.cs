using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Integration tests for FlowBuilder and DslFlowExecutor
/// </summary>
public class FlowBuilderIntegrationTests
{
    private class OrderState : BaseFlowState
    {
        public string OrderId { get; set; } = "";
        public string Status { get; set; } = "Pending";
        public decimal Amount { get; set; }
        public List<string> ProcessedItems { get; set; } = new();
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record CreateOrderCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    #region FlowConfig Integration Tests

    [Fact]
    public void FlowConfig_Build_CreatesValidBuilder()
    {
        var config = new TestOrderFlowConfig();
        config.Build();

        config.Builder.Should().NotBeNull();
        config.Builder.Steps.Should().NotBeEmpty();
    }

    [Fact]
    public void FlowConfig_Steps_MatchesBuilderSteps()
    {
        var config = new TestOrderFlowConfig();
        config.Build();

        config.Steps.Should().BeSameAs(config.Builder.Steps);
    }

    [Fact]
    public void FlowConfig_MultipleBuild_OnlyConfiguresOnce()
    {
        var config = new TestOrderFlowConfig();
        config.Build();
        var stepCount = config.Builder.Steps.Count;

        config.Build();
        config.Build();

        config.Builder.Steps.Count.Should().Be(stepCount);
    }

    private class TestOrderFlowConfig : FlowConfig<OrderState>
    {
        protected override void Configure(IFlowBuilder<OrderState> flow)
        {
            flow
                .Name("OrderFlow")
                .Send(s => new CreateOrderCommand(s.OrderId));
        }
    }

    #endregion

    #region Executor Integration Tests

    [Fact]
    public async Task Executor_EmptySteps_ReturnsCompleted()
    {
        var executor = CreateExecutor();
        var state = new OrderState { FlowId = "test-1" };

        var result = await executor.ExecuteAsync(state, new List<FlowStep>());

        result.Status.Should().Be(DslFlowStatus.Completed);
    }

    [Fact]
    public async Task Executor_PreservesState_AfterExecution()
    {
        var executor = CreateExecutor();
        var state = new OrderState
        {
            FlowId = "test-2",
            OrderId = "order-123",
            Amount = 99.99m
        };

        var result = await executor.ExecuteAsync(state, new List<FlowStep>());

        result.State.OrderId.Should().Be("order-123");
        result.State.Amount.Should().Be(99.99m);
    }

    [Fact]
    public async Task Executor_WithCancellation_ThrowsOperationCanceled()
    {
        var executor = CreateExecutor();
        var state = new OrderState { FlowId = "cancel-test" };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => executor.ExecuteAsync(state, new List<FlowStep>(), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private DslFlowExecutor<OrderState> CreateExecutor()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IMessageTransport>());
        return new DslFlowExecutor<OrderState>(services.BuildServiceProvider());
    }

    #endregion

    #region End-to-End Flow Tests

    [Fact]
    public void EndToEnd_BuildAndInspect_WorksCorrectly()
    {
        var config = new ComplexOrderFlowConfig();
        config.Build();

        config.Builder.FlowName.Should().Be("ComplexOrderFlow");
        config.Builder.Steps.Should().HaveCountGreaterThan(0);
        config.Builder.TaggedTimeouts.Should().ContainKey("api");
    }

    private class ComplexOrderFlowConfig : FlowConfig<OrderState>
    {
        protected override void Configure(IFlowBuilder<OrderState> flow)
        {
            flow
                .Name("ComplexOrderFlow")
                .Timeout(TimeSpan.FromSeconds(30)).ForTag("api")
                .Retry(3).ForTag("api")
                .Send(s => new CreateOrderCommand(s.OrderId)).Tag("api")
                .If(s => s.Amount > 100)
                    .Send(s => new CreateOrderCommand("premium"))
                .EndIf();
        }
    }

    #endregion
}
