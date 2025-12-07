using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.Stores;
using Catga.Persistence.InMemory.Stores;
using Catga.Resilience;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Unit tests for TimeTravelService.
/// </summary>
public class TimeTravelServiceTests
{
    private readonly InMemoryEventStore _eventStore;
    private readonly ITimeTravelService<CounterAggregate> _timeTravelService;

    public TimeTravelServiceTests()
    {
        _eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        _timeTravelService = new TimeTravelService<CounterAggregate>(_eventStore);
    }

    [Fact]
    public async Task GetStateAtVersionAsync_ReturnsCorrectState()
    {
        // Arrange
        await _eventStore.AppendAsync("CounterAggregate-counter-1", [
            new CounterCreated("counter-1"),
            new CounterIncremented(1),
            new CounterIncremented(1),
            new CounterIncremented(1)
        ]);

        // Act
        var state = await _timeTravelService.GetStateAtVersionAsync("counter-1", 2);

        // Assert
        state.Should().NotBeNull();
        state!.Value.Should().Be(2); // Created + 2 increments
    }

    [Fact]
    public async Task GetStateAtVersionAsync_Version0_ReturnsInitialState()
    {
        // Arrange
        await _eventStore.AppendAsync("CounterAggregate-counter-1", [
            new CounterCreated("counter-1"),
            new CounterIncremented(5)
        ]);

        // Act
        var state = await _timeTravelService.GetStateAtVersionAsync("counter-1", 0);

        // Assert
        state.Should().NotBeNull();
        state!.Value.Should().Be(0); // Only created event
    }

    [Fact]
    public async Task GetStateAtVersionAsync_NonExistentStream_ReturnsNull()
    {
        // Act
        var state = await _timeTravelService.GetStateAtVersionAsync("nonexistent", 0);

        // Assert
        state.Should().BeNull();
    }

    [Fact]
    public async Task GetStateAtVersionAsync_LatestVersion_ReturnsLatestState()
    {
        // Arrange
        await _eventStore.AppendAsync("CounterAggregate-counter-1", [
            new CounterCreated("counter-1"),
            new CounterIncremented(10),
            new CounterIncremented(5)
        ]);

        // Act - get state at latest version (2)
        var state = await _timeTravelService.GetStateAtVersionAsync("counter-1", 2);

        // Assert
        state.Should().NotBeNull();
        state!.Value.Should().Be(15);
    }

    [Fact]
    public async Task CompareVersionsAsync_ReturnsFromAndToStates()
    {
        // Arrange
        await _eventStore.AppendAsync("CounterAggregate-counter-1", [
            new CounterCreated("counter-1"),
            new CounterIncremented(1),
            new CounterIncremented(2),
            new CounterIncremented(3)
        ]);

        // Act
        var comparison = await _timeTravelService.CompareVersionsAsync("counter-1", 1, 3);

        // Assert
        comparison.FromState.Should().NotBeNull();
        comparison.ToState.Should().NotBeNull();
        comparison.FromState!.Value.Should().Be(1);
        comparison.ToState!.Value.Should().Be(6);
    }

    [Fact]
    public async Task GetVersionHistoryAsync_ReturnsAllVersions()
    {
        // Arrange
        await _eventStore.AppendAsync("CounterAggregate-counter-1", [
            new CounterCreated("counter-1"),
            new CounterIncremented(1),
            new CounterIncremented(2)
        ]);

        // Act
        var history = await _timeTravelService.GetVersionHistoryAsync("counter-1");

        // Assert
        history.Should().HaveCount(3);
    }

    #region Test helpers

    private record CounterCreated(string Id) : IEvent
    {
        public long MessageId { get; init; } = Random.Shared.NextInt64();
    }

    private record CounterIncremented(int Amount) : IEvent
    {
        public long MessageId { get; init; } = Random.Shared.NextInt64();
    }

    private class CounterAggregate : AggregateRoot
    {
        public override string Id { get; protected set; } = "";
        public int Value { get; private set; }

        protected override void When(IEvent @event)
        {
            switch (@event)
            {
                case CounterCreated e:
                    Id = e.Id;
                    break;
                case CounterIncremented e:
                    Value += e.Amount;
                    break;
            }
        }
    }

    #endregion
}
