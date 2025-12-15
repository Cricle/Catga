using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Serialization tests for FlowSnapshot
/// </summary>
public class FlowSnapshotSerializationTests
{
    private class TestState : BaseFlowState
    {
        public string Data { get; set; } = "";
        public int Counter { get; set; }
        public DateTime Timestamp { get; set; }
        public List<string> Items { get; set; } = new();
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    [Fact]
    public void FlowSnapshot_AllPropertiesSet_ArePreserved()
    {
        var now = DateTime.UtcNow;
        var snapshot = new FlowSnapshot<TestState>
        {
            FlowId = "test-flow-123",
            State = new TestState
            {
                FlowId = "test-flow-123",
                Data = "test-data",
                Counter = 42,
                Timestamp = now,
                Items = new List<string> { "a", "b", "c" }
            },
            Position = new FlowPosition(new[] { 1, 2, 3 }),
            Status = DslFlowStatus.Running,
            Error = null,
            CreatedAt = now.AddMinutes(-10),
            UpdatedAt = now,
            Version = 5
        };

        snapshot.FlowId.Should().Be("test-flow-123");
        snapshot.State.Data.Should().Be("test-data");
        snapshot.State.Counter.Should().Be(42);
        snapshot.State.Items.Should().HaveCount(3);
        snapshot.Position.Path.Should().BeEquivalentTo(new[] { 1, 2, 3 });
        snapshot.Status.Should().Be(DslFlowStatus.Running);
        snapshot.Version.Should().Be(5);
    }

    [Fact]
    public void FlowSnapshot_WithError_ErrorIsPreserved()
    {
        var snapshot = new FlowSnapshot<TestState>
        {
            FlowId = "error-flow",
            State = new TestState { FlowId = "error-flow" },
            Status = DslFlowStatus.Failed,
            Error = "Something went wrong"
        };

        snapshot.Error.Should().Be("Something went wrong");
        snapshot.Status.Should().Be(DslFlowStatus.Failed);
    }

    [Fact]
    public void FlowSnapshot_WithWaitCondition_ConditionIsPreserved()
    {
        var condition = new WaitCondition("correlation-123", "SomeEvent");
        var snapshot = new FlowSnapshot<TestState>
        {
            FlowId = "waiting-flow",
            State = new TestState { FlowId = "waiting-flow" },
            Status = DslFlowStatus.WaitingForEvent,
            WaitCondition = condition
        };

        snapshot.WaitCondition.Should().NotBeNull();
        snapshot.WaitCondition!.CorrelationId.Should().Be("correlation-123");
        snapshot.WaitCondition.Type.Should().Be("SomeEvent");
    }

    [Fact]
    public void FlowSnapshot_DefaultValues_AreCorrect()
    {
        var snapshot = new FlowSnapshot<TestState>();

        snapshot.FlowId.Should().BeNull();
        snapshot.State.Should().BeNull();
        snapshot.Status.Should().Be(DslFlowStatus.Pending);
        snapshot.Error.Should().BeNull();
        snapshot.Version.Should().Be(0);
    }

    [Fact]
    public void FlowSnapshot_VersionIncrement_Works()
    {
        var snapshot = new FlowSnapshot<TestState>
        {
            FlowId = "versioned-flow",
            State = new TestState { FlowId = "versioned-flow" },
            Version = 1
        };

        snapshot.Version.Should().Be(1);

        var updatedSnapshot = new FlowSnapshot<TestState>
        {
            FlowId = snapshot.FlowId,
            State = snapshot.State,
            Version = snapshot.Version + 1
        };

        updatedSnapshot.Version.Should().Be(2);
    }

    [Fact]
    public void FlowSnapshot_ComplexState_IsPreserved()
    {
        var state = new TestState
        {
            FlowId = "complex-flow",
            Data = "complex data with special chars: 日本語, émojis 🎉",
            Counter = int.MaxValue,
            Timestamp = DateTime.UtcNow,
            Items = Enumerable.Range(0, 100).Select(i => $"item-{i}").ToList()
        };

        var snapshot = new FlowSnapshot<TestState>
        {
            FlowId = state.FlowId,
            State = state
        };

        snapshot.State.Data.Should().Contain("日本語");
        snapshot.State.Counter.Should().Be(int.MaxValue);
        snapshot.State.Items.Should().HaveCount(100);
    }
}
