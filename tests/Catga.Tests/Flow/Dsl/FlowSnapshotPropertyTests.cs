using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowSnapshot properties
/// </summary>
public class FlowSnapshotPropertyTests
{
    private class TestState : BaseFlowState
    {
        public string Data { get; set; } = "";
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    #region FlowSnapshot Creation Tests

    [Fact]
    public void FlowSnapshot_CanBeCreated()
    {
        var snapshot = new FlowSnapshot<TestState>
        {
            FlowId = "flow-123",
            State = new TestState()
        };

        snapshot.Should().NotBeNull();
    }

    [Fact]
    public void FlowSnapshot_WithPosition_Works()
    {
        var snapshot = new FlowSnapshot<TestState>
        {
            FlowId = "flow-123",
            State = new TestState(),
            Position = new FlowPosition(new[] { 0, 1 })
        };

        snapshot.Position.Should().NotBeNull();
    }

    #endregion

    #region FlowSnapshot Property Tests

    [Fact]
    public void FlowSnapshot_FlowId_CanBeSet()
    {
        var snapshot = new FlowSnapshot<TestState>
        {
            FlowId = "flow-123"
        };

        snapshot.FlowId.Should().Be("flow-123");
    }

    [Fact]
    public void FlowSnapshot_State_CanBeSet()
    {
        var state = new TestState { Data = "test-data" };
        var snapshot = new FlowSnapshot<TestState>
        {
            State = state
        };

        snapshot.State.Should().Be(state);
    }

    [Fact]
    public void FlowSnapshot_Status_CanBeSet()
    {
        var snapshot = new FlowSnapshot<TestState>
        {
            Status = DslFlowStatus.Running
        };

        snapshot.Status.Should().Be(DslFlowStatus.Running);
    }

    #endregion

    #region FlowSnapshot Default Values Tests

    [Fact]
    public void FlowSnapshot_DefaultStatus_IsNotSet()
    {
        var snapshot = new FlowSnapshot<TestState>();

        snapshot.Status.Should().Be(default(DslFlowStatus));
    }

    #endregion
}
