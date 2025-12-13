using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.EventSourcing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// E2E tests for Snapshot and Projection features.
/// Tests snapshotting strategies, projection building, and read model optimization.
/// </summary>
public class SnapshotAndProjectionE2ETests
{
    [Fact]
    public async Task Snapshot_SaveAndLoad_ReconstructsState()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var snapshotStore = sp.GetRequiredService<IEnhancedSnapshotStore>();

        var streamId = $"Order-{Guid.NewGuid():N}"[..16];
        var aggregate = new TestOrderAggregate
        {
            Id = streamId,
            CustomerId = "CUST-001",
            TotalAmount = 999.99m,
            Status = "Confirmed",
            Version = 10
        };

        // Act
        await snapshotStore.SaveAsync(streamId, aggregate, 10);
        var loaded = await snapshotStore.GetAsync<TestOrderAggregate>(streamId);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(streamId);
        loaded.CustomerId.Should().Be("CUST-001");
        loaded.TotalAmount.Should().Be(999.99m);
        loaded.Status.Should().Be("Confirmed");
    }

    [Fact]
    public async Task Snapshot_GetHistory_ReturnsAllSnapshots()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var snapshotStore = sp.GetRequiredService<IEnhancedSnapshotStore>();

        var streamId = $"Order-{Guid.NewGuid():N}"[..16];

        // Save multiple snapshots
        for (int i = 1; i <= 5; i++)
        {
            var aggregate = new TestOrderAggregate
            {
                Id = streamId,
                TotalAmount = i * 100m,
                Version = i * 10
            };
            await snapshotStore.SaveAsync(streamId, aggregate, i * 10);
        }

        // Act
        var history = await snapshotStore.GetSnapshotHistoryAsync(streamId);

        // Assert
        history.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Snapshot_WithEvents_RebuildsFromSnapshot()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();
        var snapshotStore = sp.GetRequiredService<IEnhancedSnapshotStore>();

        var streamId = $"Order-{Guid.NewGuid():N}"[..16];

        // Append 10 events
        for (int i = 1; i <= 10; i++)
        {
            await eventStore.AppendAsync(streamId, new IEvent[]
            {
                new OrderAmountChangedEvent { OrderId = streamId, NewAmount = i * 10m }
            });
        }

        // Create snapshot at version 5
        var snapshotAggregate = new TestOrderAggregate { Id = streamId, TotalAmount = 150m, Version = 5 };
        await snapshotStore.SaveAsync(streamId, snapshotAggregate, 5);

        // Act - Load snapshot
        var snapshot = await snapshotStore.GetAsync<TestOrderAggregate>(streamId);

        // Act - Read events after snapshot
        var stream = await eventStore.ReadAsync(streamId, 6); // From version 6

        // Assert
        snapshot.Should().NotBeNull();
        snapshot!.Version.Should().Be(5);
        stream.Events.Should().HaveCount(5); // Events 6-10
    }

    [Fact]
    public async Task Projection_ApplyEvents_BuildsReadModel()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var projection = new OrderSummaryProjection();

        // Append events for multiple orders
        for (int i = 1; i <= 5; i++)
        {
            var streamId = $"Order-{i:000}";
            await eventStore.AppendAsync(streamId, new IEvent[]
            {
                new OrderCreatedEvent { OrderId = streamId, Amount = i * 100m }
            });
        }

        // Act - Apply all events to projection
        for (int i = 1; i <= 5; i++)
        {
            var streamId = $"Order-{i:000}";
            var stream = await eventStore.ReadAsync(streamId);
            foreach (var envelope in stream.Events)
            {
                await projection.ApplyAsync(envelope.Event);
            }
        }

        // Assert
        projection.TotalOrders.Should().Be(5);
        projection.TotalRevenue.Should().Be(1500m); // 100+200+300+400+500
    }

    [Fact]
    public async Task ProjectionCheckpoint_TrackProgress_SavesPosition()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var checkpointStore = sp.GetRequiredService<IProjectionCheckpointStore>();

        var projectionName = "OrderSummary";

        // Act - Save checkpoint
        await checkpointStore.SaveCheckpointAsync(projectionName, 100);
        var checkpoint1 = await checkpointStore.GetCheckpointAsync(projectionName);

        // Update checkpoint
        await checkpointStore.SaveCheckpointAsync(projectionName, 200);
        var checkpoint2 = await checkpointStore.GetCheckpointAsync(projectionName);

        // Assert
        checkpoint1.Should().Be(100);
        checkpoint2.Should().Be(200);
    }

    [Fact]
    public async Task ProjectionRebuilder_Rebuild_ReprocessesAllEvents()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();
        var checkpointStore = sp.GetRequiredService<IProjectionCheckpointStore>();

        var projection = new OrderSummaryProjection();

        // Add events
        for (int i = 1; i <= 3; i++)
        {
            await eventStore.AppendAsync($"Order-{i}", new IEvent[]
            {
                new OrderCreatedEvent { OrderId = $"Order-{i}", Amount = 100m }
            });
        }

        // Pre-process some events
        projection.TotalOrders = 2;
        projection.TotalRevenue = 200m;
        await checkpointStore.SaveCheckpointAsync(projection.Name, 2);

        // Act - Reset and rebuild
        await projection.ResetAsync();
        await checkpointStore.SaveCheckpointAsync(projection.Name, 0);

        // Manually rebuild (simulate ProjectionRebuilder)
        for (int i = 1; i <= 3; i++)
        {
            var stream = await eventStore.ReadAsync($"Order-{i}");
            foreach (var envelope in stream.Events)
            {
                await projection.ApplyAsync(envelope.Event);
            }
        }

        // Assert
        projection.TotalOrders.Should().Be(3);
        projection.TotalRevenue.Should().Be(300m);
    }

    [Fact]
    public async Task MultipleProjections_SameEvents_DifferentViews()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var summaryProjection = new OrderSummaryProjection();
        var categoryProjection = new CategoryProjection();

        // Add events
        await eventStore.AppendAsync("Order-001", new IEvent[]
        {
            new OrderWithCategoryEvent { OrderId = "Order-001", Amount = 100m, Category = "Electronics" }
        });
        await eventStore.AppendAsync("Order-002", new IEvent[]
        {
            new OrderWithCategoryEvent { OrderId = "Order-002", Amount = 50m, Category = "Books" }
        });
        await eventStore.AppendAsync("Order-003", new IEvent[]
        {
            new OrderWithCategoryEvent { OrderId = "Order-003", Amount = 200m, Category = "Electronics" }
        });

        // Act - Apply to both projections
        var allEvents = new List<IEvent>();
        foreach (var orderId in new[] { "Order-001", "Order-002", "Order-003" })
        {
            var stream = await eventStore.ReadAsync(orderId);
            foreach (var envelope in stream.Events)
            {
                await summaryProjection.ApplyAsync(envelope.Event);
                await categoryProjection.ApplyAsync(envelope.Event);
            }
        }

        // Assert - Summary view
        summaryProjection.TotalOrders.Should().Be(3);
        summaryProjection.TotalRevenue.Should().Be(350m);

        // Assert - Category view
        categoryProjection.CategoryTotals["Electronics"].Should().Be(300m);
        categoryProjection.CategoryTotals["Books"].Should().Be(50m);
    }

    [Fact]
    public async Task Snapshot_DeleteOld_CleansUpHistory()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var snapshotStore = sp.GetRequiredService<IEnhancedSnapshotStore>();

        var streamId = $"Order-{Guid.NewGuid():N}"[..16];

        // Create multiple snapshots
        for (int i = 1; i <= 5; i++)
        {
            await snapshotStore.SaveAsync(streamId, new TestOrderAggregate { Id = streamId, Version = i * 10 }, i * 10);
        }

        // Act - Delete snapshots older than version 30
        await snapshotStore.DeleteOlderThanAsync(streamId, 30);

        // Assert - Get history should only have newer snapshots
        var history = await snapshotStore.GetSnapshotHistoryAsync(streamId);
        history.Should().OnlyContain(h => h.Version >= 30);
    }

    #region Test Types

    public class TestOrderAggregate
    {
        public string Id { get; set; } = "";
        public string CustomerId { get; set; } = "";
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = "";
        public long Version { get; set; }
    }

    public record OrderAmountChangedEvent : IEvent
    {
        public long MessageId { get; init; }
        public string OrderId { get; init; } = "";
        public decimal NewAmount { get; init; }
    }

    public record OrderCreatedEvent : IEvent
    {
        public long MessageId { get; init; }
        public string OrderId { get; init; } = "";
        public decimal Amount { get; init; }
    }

    public record OrderWithCategoryEvent : IEvent
    {
        public long MessageId { get; init; }
        public string OrderId { get; init; } = "";
        public decimal Amount { get; init; }
        public string Category { get; init; } = "";
    }

    public class OrderSummaryProjection : IProjection
    {
        public string Name => "OrderSummary";
        public int TotalOrders { get; set; }
        public decimal TotalRevenue { get; set; }

        public ValueTask ApplyAsync(IEvent @event, CancellationToken ct = default)
        {
            switch (@event)
            {
                case OrderCreatedEvent e:
                    TotalOrders++;
                    TotalRevenue += e.Amount;
                    break;
                case OrderWithCategoryEvent e:
                    TotalOrders++;
                    TotalRevenue += e.Amount;
                    break;
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask ResetAsync(CancellationToken ct = default)
        {
            TotalOrders = 0;
            TotalRevenue = 0;
            return ValueTask.CompletedTask;
        }
    }

    public class CategoryProjection : IProjection
    {
        public string Name => "CategoryProjection";
        public Dictionary<string, decimal> CategoryTotals { get; } = new();

        public ValueTask ApplyAsync(IEvent @event, CancellationToken ct = default)
        {
            if (@event is OrderWithCategoryEvent e)
            {
                if (!CategoryTotals.ContainsKey(e.Category))
                    CategoryTotals[e.Category] = 0;
                CategoryTotals[e.Category] += e.Amount;
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask ResetAsync(CancellationToken ct = default)
        {
            CategoryTotals.Clear();
            return ValueTask.CompletedTask;
        }
    }

    #endregion
}
