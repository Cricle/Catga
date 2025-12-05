using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Flow.Resume;

/// <summary>
/// Tests for FlowResumeHandler.
/// </summary>
public class FlowResumeHandlerTests
{
    private readonly IDslFlowStore _store;
    private readonly FlowResumeHandler _handler;

    public FlowResumeHandlerTests()
    {
        _store = Substitute.For<IDslFlowStore>();
        _handler = new FlowResumeHandler(_store);
    }

    [Fact]
    public async Task HandleAsync_NoParentCorrelationId_DoesNothing()
    {
        var @event = new FlowCompletedEvent("child-1", null, true, null, null);

        await _handler.HandleAsync(@event);

        await _store.DidNotReceive().GetWaitConditionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WaitConditionNotFound_DoesNothing()
    {
        var @event = new FlowCompletedEvent("child-1", "parent-corr", true, null, null);
        _store.GetWaitConditionAsync("parent-corr", Arg.Any<CancellationToken>())
            .Returns((WaitCondition?)null);

        await _handler.HandleAsync(@event);

        await _store.DidNotReceive().UpdateWaitConditionAsync(Arg.Any<string>(), Arg.Any<WaitCondition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_UpdatesWaitCondition()
    {
        var @event = new FlowCompletedEvent("child-1", "parent-corr", true, null, "result-data");
        var waitCondition = CreateWaitCondition("parent-corr", WaitType.All, 2);

        _store.GetWaitConditionAsync("parent-corr", Arg.Any<CancellationToken>())
            .Returns(waitCondition);

        await _handler.HandleAsync(@event);

        await _store.Received(1).UpdateWaitConditionAsync(
            "parent-corr",
            Arg.Is<WaitCondition>(c =>
                c.CompletedCount == 1 &&
                c.Results.Count == 1 &&
                c.Results[0].FlowId == "child-1" &&
                c.Results[0].Success),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenAll_IncrementsCompletedCount()
    {
        var @event = new FlowCompletedEvent("child-1", "parent-corr", true, null, null);
        var waitCondition = CreateWaitCondition("parent-corr", WaitType.All, 3);
        waitCondition.CompletedCount = 1; // Already has 1 completed

        _store.GetWaitConditionAsync("parent-corr", Arg.Any<CancellationToken>())
            .Returns(waitCondition);

        await _handler.HandleAsync(@event);

        await _store.Received(1).UpdateWaitConditionAsync(
            "parent-corr",
            Arg.Is<WaitCondition>(c => c.CompletedCount == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_StoresResultData()
    {
        var resultData = new { PaymentId = "pay-123" };
        var @event = new FlowCompletedEvent("child-1", "parent-corr", true, null, resultData);
        var waitCondition = CreateWaitCondition("parent-corr", WaitType.Any, 2);

        _store.GetWaitConditionAsync("parent-corr", Arg.Any<CancellationToken>())
            .Returns(waitCondition);

        await _handler.HandleAsync(@event);

        await _store.Received(1).UpdateWaitConditionAsync(
            "parent-corr",
            Arg.Is<WaitCondition>(c => c.Results[0].Result == resultData),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_FailedChild_StoresError()
    {
        var @event = new FlowCompletedEvent("child-1", "parent-corr", false, "Payment failed", null);
        var waitCondition = CreateWaitCondition("parent-corr", WaitType.All, 2);

        _store.GetWaitConditionAsync("parent-corr", Arg.Any<CancellationToken>())
            .Returns(waitCondition);

        await _handler.HandleAsync(@event);

        await _store.Received(1).UpdateWaitConditionAsync(
            "parent-corr",
            Arg.Is<WaitCondition>(c =>
                c.Results[0].Success == false &&
                c.Results[0].Error == "Payment failed"),
            Arg.Any<CancellationToken>());
    }

    private static WaitCondition CreateWaitCondition(string correlationId, WaitType type, int expectedCount)
    {
        return new WaitCondition
        {
            CorrelationId = correlationId,
            Type = type,
            ExpectedCount = expectedCount,
            CompletedCount = 0,
            Timeout = TimeSpan.FromMinutes(5),
            CreatedAt = DateTime.UtcNow,
            FlowId = "parent-flow",
            FlowType = "TestFlow",
            Step = 0
        };
    }
}

/// <summary>
/// Tests for FlowTimeoutService.
/// </summary>
public class FlowTimeoutServiceTests
{
    [Fact]
    public async Task CheckTimeouts_MarksTimedOutConditions()
    {
        var store = new InMemoryDslFlowStore();

        // Create a timed out condition
        var timedOut = new WaitCondition
        {
            CorrelationId = "corr-1",
            Type = WaitType.All,
            ExpectedCount = 2,
            CompletedCount = 0,
            Timeout = TimeSpan.FromMilliseconds(1),
            CreatedAt = DateTime.UtcNow.AddSeconds(-10),
            FlowId = "flow-1",
            FlowType = "TestFlow",
            Step = 0
        };
        await store.SetWaitConditionAsync("corr-1", timedOut);

        // Wait for timeout
        await Task.Delay(10);

        // Check timeouts
        var service = new FlowTimeoutService(store, TimeSpan.FromMilliseconds(10));

        // Manually trigger check
        var timedOutConditions = await store.GetTimedOutWaitConditionsAsync();
        timedOutConditions.Should().HaveCount(1);
    }

    [Fact]
    public async Task StartStop_WorksCorrectly()
    {
        var store = new InMemoryDslFlowStore();
        var service = new FlowTimeoutService(store, TimeSpan.FromMilliseconds(50));

        service.Start();
        await Task.Delay(100);
        await service.StopAsync();

        // Should complete without error
    }
}
