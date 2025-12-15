using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowResult status and state
/// </summary>
public class FlowResultStatusTests
{
    private class TestState : BaseFlowState
    {
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    #region FlowResult Creation Tests

    [Fact]
    public void FlowResult_CanBeCreated()
    {
        var result = new FlowResult
        {
            Status = DslFlowStatus.Completed
        };

        result.Should().NotBeNull();
    }

    [Fact]
    public void FlowResult_WithStatus_Works()
    {
        var result = new FlowResult
        {
            Status = DslFlowStatus.Running
        };

        result.Status.Should().Be(DslFlowStatus.Running);
    }

    #endregion

    #region FlowResult Status Tests

    [Fact]
    public void FlowResult_StatusCanBeSet()
    {
        var result = new FlowResult();
        result.Status = DslFlowStatus.Completed;

        result.Status.Should().Be(DslFlowStatus.Completed);
    }

    [Fact]
    public void FlowResult_MultipleStatuses_AllValid()
    {
        var statuses = new[]
        {
            DslFlowStatus.Pending,
            DslFlowStatus.Running,
            DslFlowStatus.Completed,
            DslFlowStatus.Failed,
            DslFlowStatus.Cancelled
        };

        foreach (var status in statuses)
        {
            var result = new FlowResult { Status = status };
            result.Status.Should().Be(status);
        }
    }

    #endregion

    #region FlowResult Properties Tests

    [Fact]
    public void FlowResult_Position_CanBeSet()
    {
        var position = new FlowPosition(new[] { 0, 1 });
        var result = new FlowResult
        {
            Position = position
        };

        result.Position.Should().Be(position);
    }

    [Fact]
    public void FlowResult_State_CanBeSet()
    {
        var state = new TestState();
        var result = new FlowResult
        {
            State = state
        };

        result.State.Should().Be(state);
    }

    #endregion
}
