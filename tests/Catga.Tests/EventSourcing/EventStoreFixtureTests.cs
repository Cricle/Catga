using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.EventSourcing.Testing;
using Catga.Persistence.Stores;
using Catga.Resilience;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Unit tests for EventStoreFixture.
/// </summary>
public class EventStoreFixtureTests
{
    [Fact]
    public void Constructor_CreatesFixture()
    {
        // Arrange
        var eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());

        // Act
        using var fixture = new EventStoreFixture(eventStore);

        // Assert
        fixture.EventStore.Should().BeSameAs(eventStore);
    }

    [Fact]
    public async Task SeedAsync_AppendsEvents()
    {
        // Arrange
        var eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        using var fixture = new EventStoreFixture(eventStore);

        // Act
        await fixture.SeedAsync("stream-1", [new TestEvent("a"), new TestEvent("b")]);

        // Assert
        var stream = await eventStore.ReadAsync("stream-1");
        stream.Events.Should().HaveCount(2);
    }

    [Fact]
    public async Task AssertEventAppendedAsync_WithMatchingEvent_DoesNotThrow()
    {
        // Arrange
        var eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        using var fixture = new EventStoreFixture(eventStore);
        await fixture.SeedAsync("stream-1", [new TestEvent("a")]);

        // Act & Assert
        var act = async () => await fixture.AssertEventAppendedAsync<TestEvent>("stream-1");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AssertEventAppendedAsync_WithoutMatchingEvent_Throws()
    {
        // Arrange
        var eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        using var fixture = new EventStoreFixture(eventStore);

        // Act & Assert
        var act = async () => await fixture.AssertEventAppendedAsync<TestEvent>("stream-1");
        await act.Should().ThrowAsync<AssertionException>();
    }

    [Fact]
    public async Task AssertEventCountAsync_WithCorrectCount_DoesNotThrow()
    {
        // Arrange
        var eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        using var fixture = new EventStoreFixture(eventStore);
        await fixture.SeedAsync("stream-1", [new TestEvent("a"), new TestEvent("b"), new TestEvent("c")]);

        // Act & Assert
        var act = async () => await fixture.AssertEventCountAsync("stream-1", 3);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AssertEventCountAsync_WithWrongCount_Throws()
    {
        // Arrange
        var eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        using var fixture = new EventStoreFixture(eventStore);
        await fixture.SeedAsync("stream-1", [new TestEvent("a")]);

        // Act & Assert
        var act = async () => await fixture.AssertEventCountAsync("stream-1", 5);
        await act.Should().ThrowAsync<AssertionException>();
    }

    [Fact]
    public async Task AssertNoEventsAsync_WithNoEvents_DoesNotThrow()
    {
        // Arrange
        var eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        using var fixture = new EventStoreFixture(eventStore);

        // Act & Assert
        var act = async () => await fixture.AssertNoEventsAsync("empty-stream");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AssertNoEventsAsync_WithEvents_Throws()
    {
        // Arrange
        var eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        using var fixture = new EventStoreFixture(eventStore);
        await fixture.SeedAsync("stream-1", [new TestEvent("a")]);

        // Act & Assert
        var act = async () => await fixture.AssertNoEventsAsync("stream-1");
        await act.Should().ThrowAsync<AssertionException>();
    }

    [Fact]
    public void Dispose_CallsCleanup()
    {
        // Arrange
        var cleanupCalled = false;
        var eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var fixture = new EventStoreFixture(eventStore, () => cleanupCalled = true);

        // Act
        fixture.Dispose();

        // Assert
        cleanupCalled.Should().BeTrue();
    }

    private record TestEvent(string Data) : IEvent
    {
        public long MessageId { get; init; }
    }
}
