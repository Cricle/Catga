using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow;

public class FlowSnapshotTests
{
    private class TestState : BaseFlowState
    {
        public string Data { get; set; } = "";
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    [Fact]
    public void FlowSnapshot_CanBeCreated()
    {
        var snapshot = new FlowSnapshot<TestState>
        {
            FlowId = "flow-1",
            State = new TestState { Data = "test" },
            Position = new FlowPosition(new[] { 0 }),
            Status = DslFlowStatus.Running,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        snapshot.FlowId.Should().Be("flow-1");
        snapshot.State.Data.Should().Be("test");
        snapshot.Status.Should().Be(DslFlowStatus.Running);
    }

    [Fact]
    public void FlowSnapshot_DefaultValues()
    {
        var snapshot = new FlowSnapshot<TestState>();

        snapshot.FlowId.Should().BeEmpty();
        snapshot.State.Should().BeNull();
        snapshot.Error.Should().BeNull();
        snapshot.WaitCondition.Should().BeNull();
        snapshot.Version.Should().Be(0);
    }

    [Fact]
    public void FlowSnapshot_WithError()
    {
        var snapshot = new FlowSnapshot<TestState>
        {
            FlowId = "flow-error",
            State = new TestState(),
            Status = DslFlowStatus.Failed,
            Error = "Something went wrong",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        snapshot.Status.Should().Be(DslFlowStatus.Failed);
        snapshot.Error.Should().Be("Something went wrong");
    }

    [Theory]
    [InlineData(DslFlowStatus.Pending)]
    [InlineData(DslFlowStatus.Running)]
    [InlineData(DslFlowStatus.WaitingForResponse)]
    [InlineData(DslFlowStatus.Completed)]
    [InlineData(DslFlowStatus.Failed)]
    [InlineData(DslFlowStatus.Cancelled)]
    public void FlowSnapshot_AllStatusValues(DslFlowStatus status)
    {
        var snapshot = new FlowSnapshot<TestState>
        {
            FlowId = "flow-status",
            State = new TestState(),
            Status = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        snapshot.Status.Should().Be(status);
    }

    [Fact]
    public void FlowSnapshot_VersionIncrement()
    {
        var snapshot = new FlowSnapshot<TestState>
        {
            FlowId = "flow-ver",
            State = new TestState(),
            Version = 5,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        snapshot.Version.Should().Be(5);
    }

    [Fact]
    public void FlowSnapshot_Timestamps()
    {
        var created = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var updated = new DateTime(2024, 1, 1, 13, 0, 0, DateTimeKind.Utc);

        var snapshot = new FlowSnapshot<TestState>
        {
            FlowId = "flow-time",
            State = new TestState(),
            CreatedAt = created,
            UpdatedAt = updated
        };

        snapshot.CreatedAt.Should().Be(created);
        snapshot.UpdatedAt.Should().Be(updated);
    }
}
