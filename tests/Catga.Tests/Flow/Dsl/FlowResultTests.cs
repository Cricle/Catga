using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowResult
/// </summary>
public class FlowResultTests
{
    private class TestState : BaseFlowState
    {
        public string Data { get; set; } = "";
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    #region Construction Tests

    [Fact]
    public void FlowResult_Default_HasPendingStatus()
    {
        var result = new FlowResult<TestState>();
        result.Status.Should().Be(DslFlowStatus.Pending);
    }

    [Fact]
    public void FlowResult_WithState_PreservesState()
    {
        var state = new TestState { FlowId = "test", Data = "hello" };
        var result = new FlowResult<TestState> { State = state };

        result.State.FlowId.Should().Be("test");
        result.State.Data.Should().Be("hello");
    }

    [Fact]
    public void FlowResult_Completed_HasCorrectStatus()
    {
        var result = new FlowResult<TestState>
        {
            Status = DslFlowStatus.Completed,
            State = new TestState { FlowId = "completed" }
        };

        result.Status.Should().Be(DslFlowStatus.Completed);
    }

    [Fact]
    public void FlowResult_Failed_HasError()
    {
        var result = new FlowResult<TestState>
        {
            Status = DslFlowStatus.Failed,
            Error = "Something went wrong"
        };

        result.Status.Should().Be(DslFlowStatus.Failed);
        result.Error.Should().Be("Something went wrong");
    }

    #endregion

    #region Position Tests

    [Fact]
    public void FlowResult_WithPosition_PreservesPosition()
    {
        var result = new FlowResult<TestState>
        {
            Position = new FlowPosition(new[] { 1, 2, 3 })
        };

        result.Position.Path.Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    [Fact]
    public void FlowResult_WaitingForEvent_HasWaitCondition()
    {
        var result = new FlowResult<TestState>
        {
            Status = DslFlowStatus.WaitingForEvent,
            WaitCondition = new WaitCondition("corr-123", "SomeEvent")
        };

        result.Status.Should().Be(DslFlowStatus.WaitingForEvent);
        result.WaitCondition.Should().NotBeNull();
        result.WaitCondition!.CorrelationId.Should().Be("corr-123");
    }

    #endregion

    #region Status Transition Tests

    [Fact]
    public void FlowResult_AllStatuses_AreSettable()
    {
        var statuses = Enum.GetValues<DslFlowStatus>();

        foreach (var status in statuses)
        {
            var result = new FlowResult<TestState> { Status = status };
            result.Status.Should().Be(status);
        }
    }

    #endregion

    #region Concurrent Tests

    [Fact]
    public void FlowResult_ConcurrentCreation_AllValid()
    {
        var results = new System.Collections.Concurrent.ConcurrentBag<FlowResult<TestState>>();

        Parallel.For(0, 100, i =>
        {
            results.Add(new FlowResult<TestState>
            {
                Status = i % 2 == 0 ? DslFlowStatus.Completed : DslFlowStatus.Failed,
                State = new TestState { FlowId = $"flow-{i}" }
            });
        });

        results.Count.Should().Be(100);
        results.Count(r => r.Status == DslFlowStatus.Completed).Should().Be(50);
    }

    #endregion
}
