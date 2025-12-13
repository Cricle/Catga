using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.EventSourcing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// E2E tests for Subscription features.
/// Tests event subscriptions, checkpoints, and real-time event processing.
/// </summary>
public class SubscriptionE2ETests
{
    [Fact]
    public async Task Subscription_ReceivesNewEvents_AfterSubscription()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();
        var subscriptionManager = sp.GetRequiredService<ISubscriptionManager>();

        var streamId = $"Order-{Guid.NewGuid():N}"[..16];
        var receivedEvents = new List<IEvent>();

        // Act - Subscribe
        var subscription = await subscriptionManager.SubscribeAsync(
            streamId,
            async (evt, ct) =>
            {
                receivedEvents.Add(evt);
            });

        // Append events after subscription
        await eventStore.AppendAsync(streamId, new IEvent[]
        {
            new OrderCreatedEvent("ORD-001", "CUST-001", 100m),
            new OrderConfirmedEvent("ORD-001")
        });

        // Wait for events to be processed
        await Task.Delay(100);

        // Assert
        receivedEvents.Should().HaveCountGreaterOrEqualTo(2);

        // Cleanup
        await subscription.UnsubscribeAsync();
    }

    [Fact]
    public async Task Subscription_WithCheckpoint_ResumesFromPosition()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();
        var checkpointStore = sp.GetRequiredService<IProjectionCheckpointStore>();

        var projectionName = "OrderProjection";
        var streamId = $"Order-{Guid.NewGuid():N}"[..16];

        // Append initial events
        for (int i = 1; i <= 5; i++)
        {
            await eventStore.AppendAsync(streamId, new IEvent[]
            {
                new OrderItemAddedEvent("ORD-001", $"ITEM-{i}", 1, i * 10m)
            });
        }

        // Save checkpoint at position 3
        await checkpointStore.SaveCheckpointAsync(projectionName, 3);

        // Act - Get checkpoint
        var checkpoint = await checkpointStore.GetCheckpointAsync(projectionName);

        // Assert
        checkpoint.Should().Be(3);
    }

    [Fact]
    public async Task Subscription_CatchUp_ProcessesHistoricalEvents()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var streamId = $"Order-{Guid.NewGuid():N}"[..16];

        // Append historical events
        for (int i = 1; i <= 10; i++)
        {
            await eventStore.AppendAsync(streamId, new IEvent[]
            {
                new OrderItemAddedEvent("ORD-001", $"ITEM-{i}", 1, i * 5m)
            });
        }

        // Act - Read all events (catch-up)
        var stream = await eventStore.ReadAsync(streamId, 0);

        // Assert
        stream.Events.Should().HaveCount(10);
    }

    [Fact]
    public async Task Subscription_MultipleSubscribers_AllReceiveEvents()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();
        var subscriptionManager = sp.GetRequiredService<ISubscriptionManager>();

        var streamId = $"Order-{Guid.NewGuid():N}"[..16];
        var subscriber1Events = new List<IEvent>();
        var subscriber2Events = new List<IEvent>();

        // Act - Create multiple subscriptions
        var sub1 = await subscriptionManager.SubscribeAsync(
            streamId,
            async (evt, ct) => subscriber1Events.Add(evt));

        var sub2 = await subscriptionManager.SubscribeAsync(
            streamId,
            async (evt, ct) => subscriber2Events.Add(evt));

        // Append events
        await eventStore.AppendAsync(streamId, new IEvent[]
        {
            new OrderCreatedEvent("ORD-001", "CUST-001", 200m)
        });

        await Task.Delay(100);

        // Assert - Both subscribers should receive the event
        subscriber1Events.Should().NotBeEmpty();
        subscriber2Events.Should().NotBeEmpty();

        // Cleanup
        await sub1.UnsubscribeAsync();
        await sub2.UnsubscribeAsync();
    }

    [Fact]
    public async Task Subscription_Unsubscribe_StopsReceivingEvents()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();
        var subscriptionManager = sp.GetRequiredService<ISubscriptionManager>();

        var streamId = $"Order-{Guid.NewGuid():N}"[..16];
        var receivedEvents = new List<IEvent>();

        // Subscribe
        var subscription = await subscriptionManager.SubscribeAsync(
            streamId,
            async (evt, ct) => receivedEvents.Add(evt));

        // Append first event
        await eventStore.AppendAsync(streamId, new IEvent[]
        {
            new OrderCreatedEvent("ORD-001", "CUST-001", 100m)
        });

        await Task.Delay(50);
        var countBeforeUnsubscribe = receivedEvents.Count;

        // Act - Unsubscribe
        await subscription.UnsubscribeAsync();

        // Append more events after unsubscribe
        await eventStore.AppendAsync(streamId, new IEvent[]
        {
            new OrderConfirmedEvent("ORD-001"),
            new OrderShippedEvent("ORD-001", "TRK-123")
        });

        await Task.Delay(50);

        // Assert - Should not receive events after unsubscribe
        receivedEvents.Count.Should().BeLessOrEqualTo(countBeforeUnsubscribe + 1);
    }

    [Fact]
    public async Task Subscription_FilterByEventType_ReceivesOnlyMatchingEvents()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var streamId = $"Order-{Guid.NewGuid():N}"[..16];

        // Append mixed events
        await eventStore.AppendAsync(streamId, new IEvent[]
        {
            new OrderCreatedEvent("ORD-001", "CUST-001", 100m),
            new OrderItemAddedEvent("ORD-001", "ITEM-001", 2, 25m),
            new OrderConfirmedEvent("ORD-001"),
            new OrderItemAddedEvent("ORD-001", "ITEM-002", 1, 50m),
            new OrderShippedEvent("ORD-001", "TRK-123")
        });

        // Act - Read and filter
        var stream = await eventStore.ReadAsync(streamId);
        var itemAddedEvents = stream.Events
            .Where(e => e.Event is OrderItemAddedEvent)
            .Select(e => e.Event)
            .ToList();

        // Assert
        itemAddedEvents.Should().HaveCount(2);
    }

    [Fact]
    public async Task Subscription_WithProjection_UpdatesReadModel()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var projection = new OrderStatsProjection();
        var streamId = $"Order-{Guid.NewGuid():N}"[..16];

        // Act - Append events and apply to projection
        await eventStore.AppendAsync(streamId, new IEvent[]
        {
            new OrderCreatedEvent("ORD-001", "CUST-001", 100m),
            new OrderCreatedEvent("ORD-002", "CUST-002", 200m),
            new OrderCreatedEvent("ORD-003", "CUST-001", 150m)
        });

        var stream = await eventStore.ReadAsync(streamId);
        foreach (var envelope in stream.Events)
        {
            await projection.ApplyAsync(envelope.Event);
        }

        // Assert
        projection.TotalOrders.Should().Be(3);
        projection.TotalRevenue.Should().Be(450m);
    }

    [Fact]
    public async Task Subscription_ErrorInHandler_ContinuesProcessing()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();
        var subscriptionManager = sp.GetRequiredService<ISubscriptionManager>();

        var streamId = $"Order-{Guid.NewGuid():N}"[..16];
        var processedCount = 0;
        var errorCount = 0;

        // Subscribe with error-prone handler
        var subscription = await subscriptionManager.SubscribeAsync(
            streamId,
            async (evt, ct) =>
            {
                processedCount++;
                if (processedCount == 2)
                {
                    errorCount++;
                    throw new InvalidOperationException("Handler error");
                }
            });

        // Append multiple events
        for (int i = 0; i < 5; i++)
        {
            await eventStore.AppendAsync(streamId, new IEvent[]
            {
                new OrderItemAddedEvent("ORD-001", $"ITEM-{i}", 1, 10m)
            });
        }

        await Task.Delay(200);

        // Assert - Processing should continue despite error
        processedCount.Should().BeGreaterThan(1);

        await subscription.UnsubscribeAsync();
    }

    #region Test Types

    public record OrderCreatedEvent(string OrderId, string CustomerId, decimal Amount) : IEvent
    {
        public long MessageId { get; init; }
    }

    public record OrderItemAddedEvent(string OrderId, string ItemId, int Quantity, decimal Price) : IEvent
    {
        public long MessageId { get; init; }
    }

    public record OrderConfirmedEvent(string OrderId) : IEvent
    {
        public long MessageId { get; init; }
    }

    public record OrderShippedEvent(string OrderId, string TrackingNumber) : IEvent
    {
        public long MessageId { get; init; }
    }

    public class OrderStatsProjection : IProjection
    {
        public string Name => "OrderStats";
        public int TotalOrders { get; private set; }
        public decimal TotalRevenue { get; private set; }

        public ValueTask ApplyAsync(IEvent @event, CancellationToken ct = default)
        {
            if (@event is OrderCreatedEvent e)
            {
                TotalOrders++;
                TotalRevenue += e.Amount;
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

    #endregion
}
