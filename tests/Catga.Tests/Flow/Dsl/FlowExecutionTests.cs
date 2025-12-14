using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Catga.Tests.Flow.Dsl;

public class FlowExecutionTests
{
    private class TestState : BaseFlowState
    {
        public string Data { get; set; } = "";
        public bool Processed { get; set; }
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    [Fact]
    public void DslFlowExecutor_CanBeCreated_WithServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IMessageTransport>());
        var sp = services.BuildServiceProvider();

        var executor = new DslFlowExecutor<TestState>(sp);

        executor.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_EmptyFlow_ReturnsCompleted()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IMessageTransport>());
        var sp = services.BuildServiceProvider();

        var executor = new DslFlowExecutor<TestState>(sp);
        var state = new TestState { FlowId = "test-flow" };

        var result = await executor.ExecuteAsync(state, new List<FlowStep>());

        result.Status.Should().Be(DslFlowStatus.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_PreservesState()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IMessageTransport>());
        var sp = services.BuildServiceProvider();

        var executor = new DslFlowExecutor<TestState>(sp);
        var state = new TestState { FlowId = "preserve-test", Data = "original" };

        var result = await executor.ExecuteAsync(state, new List<FlowStep>());

        result.State.Data.Should().Be("original");
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellation_ThrowsOperationCanceled()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IMessageTransport>());
        var sp = services.BuildServiceProvider();

        var executor = new DslFlowExecutor<TestState>(sp);
        var state = new TestState { FlowId = "cancel-test" };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => executor.ExecuteAsync(state, new List<FlowStep>(), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void FlowResult_Completed_HasCorrectStatus()
    {
        var result = new FlowResult<TestState>
        {
            Status = DslFlowStatus.Completed,
            State = new TestState { FlowId = "completed-test" }
        };

        result.Status.Should().Be(DslFlowStatus.Completed);
        result.State.Should().NotBeNull();
    }

    [Fact]
    public void FlowResult_Failed_HasError()
    {
        var result = new FlowResult<TestState>
        {
            Status = DslFlowStatus.Failed,
            State = new TestState { FlowId = "failed-test" },
            Error = "Something went wrong"
        };

        result.Status.Should().Be(DslFlowStatus.Failed);
        result.Error.Should().NotBeNullOrEmpty();
    }
}
