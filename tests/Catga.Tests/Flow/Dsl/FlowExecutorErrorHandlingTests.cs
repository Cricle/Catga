using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Error handling tests for DslFlowExecutor
/// </summary>
public class FlowExecutorErrorHandlingTests
{
    private class TestState : BaseFlowState
    {
        public string Data { get; set; } = "";
        public bool ShouldFail { get; set; }
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    private IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IMessageTransport>());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ExecuteAsync_NullState_ThrowsArgumentNullException()
    {
        var executor = new DslFlowExecutor<TestState>(CreateServiceProvider());

        var act = () => executor.ExecuteAsync(null!, new List<FlowStep>());

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_NullSteps_ThrowsArgumentNullException()
    {
        var executor = new DslFlowExecutor<TestState>(CreateServiceProvider());
        var state = new TestState { FlowId = "test" };

        var act = () => executor.ExecuteAsync(state, null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var executor = new DslFlowExecutor<TestState>(CreateServiceProvider());
        var state = new TestState { FlowId = "cancel-test" };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => executor.ExecuteAsync(state, new List<FlowStep>(), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_EmptySteps_ReturnsCompleted()
    {
        var executor = new DslFlowExecutor<TestState>(CreateServiceProvider());
        var state = new TestState { FlowId = "empty-test" };

        var result = await executor.ExecuteAsync(state, new List<FlowStep>());

        result.Status.Should().Be(DslFlowStatus.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_ValidState_PreservesFlowId()
    {
        var executor = new DslFlowExecutor<TestState>(CreateServiceProvider());
        var flowId = "preserve-flow-id-test";
        var state = new TestState { FlowId = flowId };

        var result = await executor.ExecuteAsync(state, new List<FlowStep>());

        result.State.FlowId.Should().Be(flowId);
    }

    [Fact]
    public async Task ExecuteAsync_ValidState_PreservesData()
    {
        var executor = new DslFlowExecutor<TestState>(CreateServiceProvider());
        var state = new TestState { FlowId = "data-test", Data = "original-data" };

        var result = await executor.ExecuteAsync(state, new List<FlowStep>());

        result.State.Data.Should().Be("original-data");
    }

    [Fact]
    public void FlowResult_DefaultStatus_IsPending()
    {
        var result = new FlowResult<TestState>();
        result.Status.Should().Be(DslFlowStatus.Pending);
    }

    [Fact]
    public void FlowResult_CanSetError()
    {
        var result = new FlowResult<TestState>
        {
            Status = DslFlowStatus.Failed,
            Error = "Test error message"
        };

        result.Error.Should().Be("Test error message");
    }

    [Fact]
    public void FlowResult_CanSetPosition()
    {
        var result = new FlowResult<TestState>
        {
            Position = new FlowPosition(new[] { 0, 1, 2 })
        };

        result.Position.Path.Should().BeEquivalentTo(new[] { 0, 1, 2 });
    }
}
