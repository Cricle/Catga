using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.InMemory.Stores;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Unit tests for EnhancedSnapshotStore.
/// </summary>
public class EnhancedSnapshotStoreTests
{
    private readonly EnhancedInMemorySnapshotStore _store = new();

    [Fact]
    public async Task SaveAsync_StoresSnapshot()
    {
        // Arrange
        var state = new TestAggregate { Value = 100 };

        // Act
        await _store.SaveAsync("stream-1", state, 5);

        // Assert
        var loaded = await _store.LoadAsync<TestAggregate>("stream-1");
        loaded.HasValue.Should().BeTrue();
        loaded.Value.Version.Should().Be(5);
    }

    [Fact]
    public async Task SaveAsync_StoresMultipleVersions()
    {
        // Arrange & Act
        var state1 = new TestAggregate { Value = 100 };
        var state2 = new TestAggregate { Value = 200 };
        var state3 = new TestAggregate { Value = 300 };

        await _store.SaveAsync("stream-1", state1, 5);
        await _store.SaveAsync("stream-1", state2, 10);
        await _store.SaveAsync("stream-1", state3, 15);

        // Assert
        var history = await _store.GetSnapshotHistoryAsync("stream-1");
        history.Should().HaveCount(3);
        history.Should().Contain(h => h.Version == 5);
        history.Should().Contain(h => h.Version == 10);
        history.Should().Contain(h => h.Version == 15);
    }

    [Fact]
    public async Task LoadAtVersionAsync_LoadsExactVersion()
    {
        // Arrange
        await _store.SaveAsync("stream-1", new TestAggregate { Value = 100 }, 5);
        await _store.SaveAsync("stream-1", new TestAggregate { Value = 200 }, 10);
        await _store.SaveAsync("stream-1", new TestAggregate { Value = 300 }, 15);

        // Act
        var snapshot = await _store.LoadAtVersionAsync<TestAggregate>("stream-1", 10);

        // Assert
        snapshot.HasValue.Should().BeTrue();
        snapshot.Value.Version.Should().Be(10);
        snapshot.Value.State.Value.Should().Be(200);
    }

    [Fact]
    public async Task LoadAtVersionAsync_LoadsClosestLowerVersion()
    {
        // Arrange
        await _store.SaveAsync("stream-1", new TestAggregate { Value = 100 }, 5);
        await _store.SaveAsync("stream-1", new TestAggregate { Value = 200 }, 10);
        await _store.SaveAsync("stream-1", new TestAggregate { Value = 300 }, 15);

        // Act - request version 12, should get version 10
        var snapshot = await _store.LoadAtVersionAsync<TestAggregate>("stream-1", 12);

        // Assert
        snapshot.HasValue.Should().BeTrue();
        snapshot.Value.Version.Should().Be(10);
    }

    [Fact]
    public async Task LoadAtVersionAsync_ReturnsNoneIfVersionTooLow()
    {
        // Arrange
        await _store.SaveAsync("stream-1", new TestAggregate { Value = 100 }, 10);

        // Act - request version 5, but earliest is 10
        var snapshot = await _store.LoadAtVersionAsync<TestAggregate>("stream-1", 5);

        // Assert
        snapshot.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_RemovesAllSnapshots()
    {
        // Arrange
        await _store.SaveAsync("stream-1", new TestAggregate { Value = 100 }, 5);
        await _store.SaveAsync("stream-1", new TestAggregate { Value = 200 }, 10);

        // Act
        await _store.DeleteAsync("stream-1");

        // Assert
        var loaded = await _store.LoadAsync<TestAggregate>("stream-1");
        loaded.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_RemovesAllVersions()
    {
        // Arrange
        await _store.SaveAsync("stream-1", new TestAggregate { Value = 100 }, 5);
        await _store.SaveAsync("stream-1", new TestAggregate { Value = 200 }, 10);
        await _store.SaveAsync("stream-1", new TestAggregate { Value = 300 }, 15);

        // Act
        await _store.DeleteAsync("stream-1");

        // Assert
        var history = await _store.GetSnapshotHistoryAsync("stream-1");
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSnapshotHistoryAsync_ReturnsEmptyForNonExistentStream()
    {
        // Act
        var history = await _store.GetSnapshotHistoryAsync("non-existent");

        // Assert
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_ReturnsLatestSnapshot()
    {
        // Arrange
        await _store.SaveAsync("stream-1", new TestAggregate { Value = 100 }, 5);
        await _store.SaveAsync("stream-1", new TestAggregate { Value = 200 }, 10);
        await _store.SaveAsync("stream-1", new TestAggregate { Value = 300 }, 15);

        // Act
        var snapshot = await _store.LoadAsync<TestAggregate>("stream-1");

        // Assert
        snapshot.HasValue.Should().BeTrue();
        snapshot.Value.Version.Should().Be(15);
        snapshot.Value.State.Value.Should().Be(300);
    }

    #region Test helpers

    private class TestAggregate : AggregateRoot
    {
        private string _id = "";
        public override string Id { get => _id; protected set => _id = value; }
        public int Value { get; set; }

        protected override void When(IEvent @event) { }
    }

    #endregion
}
