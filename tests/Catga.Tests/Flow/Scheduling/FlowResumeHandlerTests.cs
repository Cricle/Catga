using Catga.Flow;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Flow.Scheduling;

/// <summary>
/// TDD tests for IFlowResumeHandler interface.
/// </summary>
public class FlowResumeHandlerTests
{
    [Fact]
    public async Task ResumeFlowAsync_ShouldBeCalledWithCorrectParameters()
    {
        // Arrange
        var handler = Substitute.For<IFlowResumeHandler>();
        var flowId = "flow-123";
        var stateId = "state-456";

        // Act
        await handler.ResumeFlowAsync(flowId, stateId);

        // Assert
        await handler.Received(1).ResumeFlowAsync(flowId, stateId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeFlowAsync_ShouldSupportCancellation()
    {
        // Arrange
        var handler = Substitute.For<IFlowResumeHandler>();
        var cts = new CancellationTokenSource();

        // Act
        await handler.ResumeFlowAsync("flow-1", "state-1", cts.Token);

        // Assert
        await handler.Received(1).ResumeFlowAsync("flow-1", "state-1", cts.Token);
    }

    [Fact]
    public async Task ResumeFlowAsync_ShouldHandleMultipleResumes()
    {
        // Arrange
        var handler = Substitute.For<IFlowResumeHandler>();
        var resumedFlows = new List<string>();

        handler.ResumeFlowAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                resumedFlows.Add(callInfo.ArgAt<string>(0));
                return ValueTask.CompletedTask;
            });

        // Act
        await handler.ResumeFlowAsync("flow-1", "state-1");
        await handler.ResumeFlowAsync("flow-2", "state-2");
        await handler.ResumeFlowAsync("flow-3", "state-3");

        // Assert
        resumedFlows.Should().HaveCount(3);
        resumedFlows.Should().Contain(["flow-1", "flow-2", "flow-3"]);
    }

    [Fact]
    public async Task ResumeFlowAsync_ShouldPropagateExceptions()
    {
        // Arrange
        var handler = Substitute.For<IFlowResumeHandler>();
        handler.ResumeFlowAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask>(_ => throw new InvalidOperationException("Flow not found"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ResumeFlowAsync("invalid-flow", "state-1").AsTask());
    }
}
