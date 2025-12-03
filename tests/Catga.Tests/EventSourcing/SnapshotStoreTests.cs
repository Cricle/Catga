using Catga.EventSourcing;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

public class SnapshotStoreTests
{
    [Fact]
    public void Snapshot_StoresAllProperties()
    {
        var state = new TestAggregate { Id = "agg-1", Name = "Test" };
        var timestamp = DateTime.UtcNow;

        var snapshot = new Snapshot<TestAggregate>
        {
            StreamId = "stream-1",
            State = state,
            Version = 10,
            Timestamp = timestamp
        };

        snapshot.StreamId.Should().Be("stream-1");
        snapshot.State.Should().Be(state);
        snapshot.Version.Should().Be(10);
        snapshot.Timestamp.Should().Be(timestamp);
    }

    [Fact]
    public void EventCountSnapshotStrategy_ShouldTakeSnapshot_WhenThresholdReached()
    {
        var strategy = new EventCountSnapshotStrategy(100);

        strategy.ShouldTakeSnapshot(100, 0).Should().BeTrue();
        strategy.ShouldTakeSnapshot(200, 100).Should().BeTrue();
        strategy.ShouldTakeSnapshot(150, 100).Should().BeFalse();
        strategy.ShouldTakeSnapshot(99, 0).Should().BeFalse();
    }

    [Fact]
    public void EventCountSnapshotStrategy_CustomThreshold()
    {
        var strategy = new EventCountSnapshotStrategy(50);

        strategy.ShouldTakeSnapshot(50, 0).Should().BeTrue();
        strategy.ShouldTakeSnapshot(49, 0).Should().BeFalse();
    }

    [Fact]
    public void SnapshotOptions_HasSensibleDefaults()
    {
        var options = new SnapshotOptions();

        options.EventThreshold.Should().Be(100);
        options.AutoSnapshot.Should().BeTrue();
        options.KeyPrefix.Should().Be("catga:snapshot:");
    }

    [Fact]
    public void SnapshotOptions_CanBeCustomized()
    {
        var options = new SnapshotOptions
        {
            EventThreshold = 50,
            AutoSnapshot = false,
            KeyPrefix = "myapp:snapshot:"
        };

        options.EventThreshold.Should().Be(50);
        options.AutoSnapshot.Should().BeFalse();
        options.KeyPrefix.Should().Be("myapp:snapshot:");
    }

    private class TestAggregate
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
