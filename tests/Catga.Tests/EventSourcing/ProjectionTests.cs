using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.Stores;
using Catga.Persistence.InMemory.Stores;
using Catga.Resilience;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// TDD tests for event projection functionality.
/// Projections transform event streams into read models.
/// </summary>
public class ProjectionTests
{
    private readonly InMemoryEventStore _eventStore;
    private readonly InMemoryProjectionCheckpointStore _checkpointStore;

    public ProjectionTests()
    {
        _eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        _checkpointStore = new InMemoryProjectionCheckpointStore();
    }

    #region 1. IProjection interface and basic projection

    [Fact]
    public async Task Projection_AppliesEventsToReadModel()
    {
        // Arrange
        var projection = new OrderSummaryProjection();
        var events = new IEvent[]
        {
            new OrderCreatedEvent { AggregateId = "order-1", CustomerId = "cust-1", TotalAmount = 100 },
            new OrderItemAddedEvent { AggregateId = "order-1", ProductId = "prod-1", Quantity = 2 },
            new OrderCreatedEvent { AggregateId = "order-2", CustomerId = "cust-1", TotalAmount = 200 }
        };

        // Act
        foreach (var evt in events)
        {
            await projection.ApplyAsync(evt);
        }

        // Assert
        var summary = projection.GetSummary("cust-1");
        summary.Should().NotBeNull();
        summary!.TotalOrders.Should().Be(2);
        summary.TotalAmount.Should().Be(300);
    }

    [Fact]
    public async Task Projection_HandlesUnknownEventTypes()
    {
        // Arrange
        var projection = new OrderSummaryProjection();
        var unknownEvent = new UnknownTestEvent { AggregateId = "test" };

        // Act - Should not throw
        await projection.ApplyAsync(unknownEvent);

        // Assert - No state change
        projection.GetAllSummaries().Should().BeEmpty();
    }

    #endregion

    #region 2. Projection checkpoint management

    [Fact]
    public async Task CheckpointStore_SavesAndLoadsCheckpoint()
    {
        // Arrange
        var projectionName = "OrderSummary";
        var checkpoint = new ProjectionCheckpoint
        {
            ProjectionName = projectionName,
            Position = 100,
            LastUpdated = DateTime.UtcNow
        };

        // Act
        await _checkpointStore.SaveAsync(checkpoint);
        var loaded = await _checkpointStore.LoadAsync(projectionName);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Position.Should().Be(100);
    }

    [Fact]
    public async Task CheckpointStore_ReturnsNullForUnknownProjection()
    {
        // Act
        var loaded = await _checkpointStore.LoadAsync("NonExistent");

        // Assert
        loaded.Should().BeNull();
    }

    #endregion

    #region 3. Catch-up projection (replay from beginning)

    [Fact]
    public async Task CatchUpProjection_ReplaysAllEvents()
    {
        // Arrange
        var streamId = "Order-order-1";
        var events = new IEvent[]
        {
            new OrderCreatedEvent { AggregateId = "order-1", CustomerId = "cust-1", TotalAmount = 100 },
            new OrderItemAddedEvent { AggregateId = "order-1", ProductId = "prod-1", Quantity = 2 }
        };
        await _eventStore.AppendAsync(streamId, events);

        var projection = new OrderSummaryProjection();
        var runner = new CatchUpProjectionRunner<OrderSummaryProjection>(
            _eventStore, _checkpointStore, projection, "OrderSummary");

        // Act
        await runner.RunAsync();

        // Assert
        var summary = projection.GetSummary("cust-1");
        summary.Should().NotBeNull();
        summary!.TotalOrders.Should().Be(1);
    }

    [Fact]
    public async Task CatchUpProjection_ResumesFromCheckpoint()
    {
        // Arrange
        var streamId = "Order-order-1";
        var events = new IEvent[]
        {
            new OrderCreatedEvent { AggregateId = "order-1", CustomerId = "cust-1", TotalAmount = 100 },
            new OrderCreatedEvent { AggregateId = "order-2", CustomerId = "cust-1", TotalAmount = 200 }
        };
        await _eventStore.AppendAsync(streamId, events);

        // Save checkpoint at position 0 (already processed first event at version 0)
        await _checkpointStore.SaveAsync(new ProjectionCheckpoint
        {
            ProjectionName = "OrderSummary",
            Position = 0, // Version 0 already processed
            LastUpdated = DateTime.UtcNow
        });

        var projection = new OrderSummaryProjection();
        var runner = new CatchUpProjectionRunner<OrderSummaryProjection>(
            _eventStore, _checkpointStore, projection, "OrderSummary");

        // Act
        await runner.RunAsync();

        // Assert - Only second event processed (version 1)
        var summary = projection.GetSummary("cust-1");
        summary.Should().NotBeNull();
        summary!.TotalOrders.Should().Be(1); // Only order-2
        summary.TotalAmount.Should().Be(200);
    }

    #endregion

    #region 4. Live projection (real-time updates)

    [Fact]
    public async Task LiveProjection_UpdatesOnNewEvents()
    {
        // Arrange
        var projection = new OrderSummaryProjection();
        var liveProjection = new LiveProjection<OrderSummaryProjection>(projection);

        // Act - Simulate live event
        await liveProjection.HandleAsync(new OrderCreatedEvent
        {
            AggregateId = "order-1",
            CustomerId = "cust-1",
            TotalAmount = 100
        });

        // Assert
        var summary = projection.GetSummary("cust-1");
        summary.Should().NotBeNull();
        summary!.TotalOrders.Should().Be(1);
    }

    #endregion

    #region 5. Projection rebuild

    [Fact]
    public async Task ProjectionRebuilder_RebuildsFromScratch()
    {
        // Arrange
        var streamId = "Order-order-1";
        var events = new IEvent[]
        {
            new OrderCreatedEvent { AggregateId = "order-1", CustomerId = "cust-1", TotalAmount = 100 },
            new OrderCreatedEvent { AggregateId = "order-2", CustomerId = "cust-2", TotalAmount = 200 }
        };
        await _eventStore.AppendAsync(streamId, events);

        // Existing checkpoint
        await _checkpointStore.SaveAsync(new ProjectionCheckpoint
        {
            ProjectionName = "OrderSummary",
            Position = 100,
            LastUpdated = DateTime.UtcNow
        });

        var projection = new OrderSummaryProjection();
        var rebuilder = new ProjectionRebuilder<OrderSummaryProjection>(
            _eventStore, _checkpointStore, projection, "OrderSummary");

        // Act - Rebuild resets checkpoint and replays all
        await rebuilder.RebuildAsync();

        // Assert
        projection.GetSummary("cust-1").Should().NotBeNull();
        projection.GetSummary("cust-2").Should().NotBeNull();

        var checkpoint = await _checkpointStore.LoadAsync("OrderSummary");
        checkpoint!.Position.Should().Be(2); // Processed 2 events
    }

    #endregion

    #region Test Domain

    private record OrderCreatedEvent : IEvent
    {
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public long MessageId { get; init; } = Random.Shared.NextInt64();
        public long? CorrelationId { get; init; }
        public long? CausationId { get; init; }
        public required string AggregateId { get; init; }
        public required string CustomerId { get; init; }
        public required decimal TotalAmount { get; init; }
    }

    private record OrderItemAddedEvent : IEvent
    {
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public long MessageId { get; init; } = Random.Shared.NextInt64();
        public long? CorrelationId { get; init; }
        public long? CausationId { get; init; }
        public required string AggregateId { get; init; }
        public required string ProductId { get; init; }
        public required int Quantity { get; init; }
    }

    private record UnknownTestEvent : IEvent
    {
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public long MessageId { get; init; } = Random.Shared.NextInt64();
        public long? CorrelationId { get; init; }
        public long? CausationId { get; init; }
        public required string AggregateId { get; init; }
    }

    /// <summary>Example projection: Customer order summary.</summary>
    private class OrderSummaryProjection : IProjection
    {
        private readonly Dictionary<string, CustomerOrderSummary> _summaries = new();

        public string Name => "OrderSummary";

        public ValueTask ApplyAsync(IEvent @event, CancellationToken ct = default)
        {
            switch (@event)
            {
                case OrderCreatedEvent e:
                    if (!_summaries.TryGetValue(e.CustomerId, out var summary))
                    {
                        summary = new CustomerOrderSummary { CustomerId = e.CustomerId };
                        _summaries[e.CustomerId] = summary;
                    }
                    summary.TotalOrders++;
                    summary.TotalAmount += e.TotalAmount;
                    break;
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask ResetAsync(CancellationToken ct = default)
        {
            _summaries.Clear();
            return ValueTask.CompletedTask;
        }

        public CustomerOrderSummary? GetSummary(string customerId)
            => _summaries.TryGetValue(customerId, out var s) ? s : null;

        public IReadOnlyList<CustomerOrderSummary> GetAllSummaries()
            => _summaries.Values.ToList();
    }

    private class CustomerOrderSummary
    {
        public required string CustomerId { get; init; }
        public int TotalOrders { get; set; }
        public decimal TotalAmount { get; set; }
    }

    #endregion
}
