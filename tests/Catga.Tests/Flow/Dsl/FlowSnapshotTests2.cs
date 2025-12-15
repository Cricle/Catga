using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Additional tests for FlowSnapshot
/// </summary>
public class FlowSnapshotTests2
{
    private class TestState : BaseFlowState
    {
        public string Data { get; set; } = "";
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    #region Snapshot Creation Tests

    [Fact]
    public void FlowSnapshot_CanBeCreated()
    {
        var state = new TestState { Data = "test" };
        var position = new FlowPosition(new[] { 0 });
        var snapshot = new FlowSnapshot<TestState>(state, position, DslFlowStatus.Running);

        snapshot.Should().NotBeNull();
        snapshot.State.Should().Be(state);
        snapshot.Position.Should().Be(position);
        snapshot.Status.Should().Be(DslFlowStatus.Running);
    }

    [Fact]
    public void FlowSnapshot_PreservesState()
    {
        var state = new TestState { Data = "preserved" };
        var snapshot = new FlowSnapshot<TestState>(state, new FlowPosition(new[] { 0 }), DslFlowStatus.Running);

        snapshot.State.Data.Should().Be("preserved");
    }

    #endregion

    #region Snapshot Position Tests

    [Fact]
    public void FlowSnapshot_PositionIsSet()
    {
        var position = new FlowPosition(new[] { 1, 2, 3 });
        var snapshot = new FlowSnapshot<TestState>(new TestState(), position, DslFlowStatus.Running);

        snapshot.Position.Path.Should().Equal(1, 2, 3);
    }

    #endregion

    #region Snapshot Status Tests

    [Fact]
    public void FlowSnapshot_StatusIsRunning()
    {
        var snapshot = new FlowSnapshot<TestState>(new TestState(), new FlowPosition(new[] { 0 }), DslFlowStatus.Running);

        snapshot.Status.Should().Be(DslFlowStatus.Running);
    }

    [Fact]
    public void FlowSnapshot_StatusIsCompleted()
    {
        var snapshot = new FlowSnapshot<TestState>(new TestState(), new FlowPosition(new[] { 0 }), DslFlowStatus.Completed);

        snapshot.Status.Should().Be(DslFlowStatus.Completed);
    }

    [Fact]
    public void FlowSnapshot_StatusIsFailed()
    {
        var snapshot = new FlowSnapshot<TestState>(new TestState(), new FlowPosition(new[] { 0 }), DslFlowStatus.Failed);

        snapshot.Status.Should().Be(DslFlowStatus.Failed);
    }

    #endregion
}
