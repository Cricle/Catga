using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.EventSourcing.Testing;
using Catga.Persistence.Stores;
using Catga.Persistence.InMemory.Stores;
using Catga.Resilience;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// TDD tests for event sourcing testing utilities.
/// </summary>
public class TestingUtilitiesTests
{
    private static EventStoreFixture CreateFixture()
    {
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        return new EventStoreFixture(store, store.Clear);
    }

    #region 1. Test fixtures

    [Fact]
    public async Task EventStoreFixture_ProvidesCleanStore()
    {
        // Arrange & Act
        using var fixture = CreateFixture();

        // Assert
        fixture.EventStore.Should().NotBeNull();
        var streams = await fixture.EventStore.GetAllStreamIdsAsync();
        streams.Should().BeEmpty();
    }

    [Fact]
    public async Task EventStoreFixture_SeedsWithEvents()
    {
        // Arrange
        using var fixture = CreateFixture();

        // Act
        await fixture.SeedAsync("Order-order-1", [
            new TestOrderCreated { OrderId = "order-1" },
            new TestOrderShipped { OrderId = "order-1" }
        ]);

        // Assert
        var stream = await fixture.EventStore.ReadAsync("Order-order-1");
        stream.Events.Should().HaveCount(2);
    }

    [Fact]
    public async Task AggregateFixture_CreatesAggregateWithHistory()
    {
        // Arrange
        var fixture = new AggregateFixture<TestOrderAggregate>();

        // Act
        fixture.Given(
            new TestOrderCreated { OrderId = "order-1" },
            new TestOrderShipped { OrderId = "order-1" }
        );

        // Assert
        fixture.Aggregate.IsShipped.Should().BeTrue();
    }

    #endregion

    #region 2. Assertion helpers

    [Fact]
    public async Task EventAssertions_AssertEventAppended()
    {
        // Arrange
        using var fixture = CreateFixture();
        await fixture.EventStore.AppendAsync("Order-order-1", [new TestOrderCreated { OrderId = "order-1" }]);

        // Act & Assert
        await fixture.AssertEventAppendedAsync<TestOrderCreated>("Order-order-1");
    }

    [Fact]
    public async Task EventAssertions_AssertEventCount()
    {
        // Arrange
        using var fixture = CreateFixture();
        await fixture.EventStore.AppendAsync("Order-order-1", [
            new TestOrderCreated { OrderId = "order-1" },
            new TestOrderShipped { OrderId = "order-1" }
        ]);

        // Act & Assert
        await fixture.AssertEventCountAsync("Order-order-1", 2);
    }

    [Fact]
    public async Task EventAssertions_AssertNoEvents()
    {
        // Arrange
        using var fixture = CreateFixture();

        // Act & Assert
        await fixture.AssertNoEventsAsync("Order-nonexistent");
    }

    [Fact]
    public void AggregateAssertions_AssertUncommittedEvent()
    {
        // Arrange
        var fixture = new AggregateFixture<TestOrderAggregate>();
        fixture.Given(new TestOrderCreated { OrderId = "order-1" });

        // Act
        fixture.Aggregate.Ship();

        // Assert
        fixture.AssertUncommittedEvent<TestOrderShipped>();
    }

    #endregion

    #region 3. Event replay testing

    [Fact]
    public async Task ReplayTester_ReplaysEventsToAggregate()
    {
        // Arrange
        using var fixture = CreateFixture();
        await fixture.SeedAsync("Order-order-1", [
            new TestOrderCreated { OrderId = "order-1" },
            new TestOrderShipped { OrderId = "order-1" }
        ]);

        var tester = new ReplayTester<TestOrderAggregate>(fixture.EventStore);

        // Act
        var aggregate = await tester.ReplayAsync("Order-order-1");

        // Assert
        aggregate.Should().NotBeNull();
        aggregate!.IsShipped.Should().BeTrue();
    }

    [Fact]
    public async Task ReplayTester_ReplaysToSpecificVersion()
    {
        // Arrange
        using var fixture = CreateFixture();
        await fixture.SeedAsync("Order-order-1", [
            new TestOrderCreated { OrderId = "order-1" },
            new TestOrderShipped { OrderId = "order-1" },
            new TestOrderDelivered { OrderId = "order-1" }
        ]);

        var tester = new ReplayTester<TestOrderAggregate>(fixture.EventStore);

        // Act - Replay only first 2 events
        var aggregate = await tester.ReplayToVersionAsync("Order-order-1", 1);

        // Assert
        aggregate.Should().NotBeNull();
        aggregate!.IsShipped.Should().BeTrue();
        aggregate.IsDelivered.Should().BeFalse();
    }

    [Fact]
    public async Task ScenarioRunner_ExecutesGivenWhenThen()
    {
        // Arrange
        using var fixture = CreateFixture();
        var scenario = new ScenarioRunner<TestOrderAggregate>(fixture.EventStore);

        // Act & Assert
        await scenario
            .Given("Order-order-1", new TestOrderCreated { OrderId = "order-1" })
            .When(agg => agg.Ship())
            .Then(agg => agg.IsShipped.Should().BeTrue())
            .RunAsync();
    }

    #endregion

    #region Test domain

    private record TestOrderCreated : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
        public string OrderId { get; init; } = "";
    }

    private record TestOrderShipped : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
        public string OrderId { get; init; } = "";
    }

    private record TestOrderDelivered : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
        public string OrderId { get; init; } = "";
    }

    private class TestOrderAggregate : AggregateRoot
    {
        private string _id = "";
        public override string Id { get => _id; protected set => _id = value; }
        public bool IsShipped { get; private set; }
        public bool IsDelivered { get; private set; }

        public void Ship()
        {
            if (IsShipped) return;
            RaiseEvent(new TestOrderShipped { OrderId = Id });
        }

        protected override void When(IEvent @event)
        {
            switch (@event)
            {
                case TestOrderCreated e:
                    _id = e.OrderId;
                    break;
                case TestOrderShipped:
                    IsShipped = true;
                    break;
                case TestOrderDelivered:
                    IsDelivered = true;
                    break;
            }
        }
    }

    #endregion
}
