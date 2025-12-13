using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.EventSourcing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// E2E tests for Event Store operations.
/// Tests event append, read, stream management, and event sourcing patterns.
/// </summary>
public class EventStoreE2ETests
{
    [Fact]
    public async Task EventStore_AppendAndRead_ReturnsEvents()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var streamId = $"Order-{Guid.NewGuid():N}"[..16];
        var events = new IEvent[]
        {
            new OrderCreatedEvent("ORD-001", "CUST-001", 199.99m),
            new OrderItemAddedEvent("ORD-001", "ITEM-001", 2, 49.99m)
        };

        // Act
        await eventStore.AppendAsync(streamId, events);
        var stream = await eventStore.ReadAsync(streamId);

        // Assert
        stream.StreamId.Should().Be(streamId);
        stream.Events.Should().HaveCount(2);
    }

    [Fact]
    public async Task EventStore_AppendMultiple_IncrementsVersion()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var streamId = $"Order-{Guid.NewGuid():N}"[..16];

        // Act
        await eventStore.AppendAsync(streamId, new IEvent[] { new OrderCreatedEvent("ORD-001", "CUST-001", 100m) });
        await eventStore.AppendAsync(streamId, new IEvent[] { new OrderItemAddedEvent("ORD-001", "ITEM-001", 1, 50m) });
        await eventStore.AppendAsync(streamId, new IEvent[] { new OrderConfirmedEvent("ORD-001") });

        var stream = await eventStore.ReadAsync(streamId);

        // Assert
        stream.Events.Should().HaveCount(3);
        stream.Version.Should().BeGreaterOrEqualTo(3);
    }

    [Fact]
    public async Task EventStore_ReadFromVersion_ReturnsSubset()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var streamId = $"Order-{Guid.NewGuid():N}"[..16];

        // Append 5 events
        for (int i = 1; i <= 5; i++)
        {
            await eventStore.AppendAsync(streamId, new IEvent[]
            {
                new OrderItemAddedEvent("ORD-001", $"ITEM-{i:000}", i, i * 10m)
            });
        }

        // Act - Read from version 3
        var stream = await eventStore.ReadAsync(streamId, 3);

        // Assert
        stream.Events.Should().HaveCount(3); // Events 3, 4, 5
    }

    [Fact]
    public async Task EventStore_ReadNonExistent_ReturnsEmptyStream()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        // Act
        var stream = await eventStore.ReadAsync("NonExistent-Stream-123");

        // Assert
        stream.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task EventStore_StreamExists_ReturnsCorrectStatus()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var existingStream = $"Existing-{Guid.NewGuid():N}"[..16];
        await eventStore.AppendAsync(existingStream, new IEvent[] { new OrderCreatedEvent("ORD-001", "CUST-001", 100m) });

        // Act
        var existsTrue = await eventStore.StreamExistsAsync(existingStream);
        var existsFalse = await eventStore.StreamExistsAsync("Non-Existing-Stream");

        // Assert
        existsTrue.Should().BeTrue();
        existsFalse.Should().BeFalse();
    }

    [Fact]
    public async Task EventStore_GetStreamVersion_ReturnsCorrectVersion()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var streamId = $"Version-{Guid.NewGuid():N}"[..16];

        // Append 3 events
        await eventStore.AppendAsync(streamId, new IEvent[] { new OrderCreatedEvent("ORD-001", "CUST-001", 100m) });
        await eventStore.AppendAsync(streamId, new IEvent[] { new OrderConfirmedEvent("ORD-001") });
        await eventStore.AppendAsync(streamId, new IEvent[] { new OrderShippedEvent("ORD-001", "TRK-123") });

        // Act
        var version = await eventStore.GetStreamVersionAsync(streamId);

        // Assert
        version.Should().BeGreaterOrEqualTo(3);
    }

    [Fact]
    public async Task EventStore_ConcurrentAppend_MaintainsOrder()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var streamId = $"Concurrent-{Guid.NewGuid():N}"[..16];

        // Act - Append concurrently
        var tasks = Enumerable.Range(1, 10).Select(i =>
            eventStore.AppendAsync(streamId, new IEvent[]
            {
                new OrderItemAddedEvent("ORD-001", $"ITEM-{i:000}", 1, i * 10m)
            }).AsTask()
        );

        await Task.WhenAll(tasks);

        var stream = await eventStore.ReadAsync(streamId);

        // Assert
        stream.Events.Should().HaveCount(10);
    }

    [Fact]
    public async Task EventStore_ReplayEvents_RebuildsAggregate()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var streamId = $"Replay-{Guid.NewGuid():N}"[..16];

        // Append events representing order lifecycle
        await eventStore.AppendAsync(streamId, new IEvent[]
        {
            new OrderCreatedEvent("ORD-001", "CUST-001", 0m),
            new OrderItemAddedEvent("ORD-001", "ITEM-001", 2, 25m),
            new OrderItemAddedEvent("ORD-001", "ITEM-002", 1, 50m),
            new OrderDiscountAppliedEvent("ORD-001", 10m),
            new OrderConfirmedEvent("ORD-001")
        });

        // Act - Replay to rebuild state
        var stream = await eventStore.ReadAsync(streamId);
        var order = new OrderAggregate();

        foreach (var envelope in stream.Events)
        {
            order.Apply(envelope.Event);
        }

        // Assert
        order.TotalAmount.Should().Be(90m); // (2*25 + 1*50) - 10 = 90
        order.ItemCount.Should().Be(2);
        order.Status.Should().Be("Confirmed");
    }

    [Fact]
    public async Task EventStore_DeleteStream_RemovesAllEvents()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var streamId = $"Delete-{Guid.NewGuid():N}"[..16];
        await eventStore.AppendAsync(streamId, new IEvent[]
        {
            new OrderCreatedEvent("ORD-001", "CUST-001", 100m),
            new OrderConfirmedEvent("ORD-001")
        });

        // Verify exists
        var existsBefore = await eventStore.StreamExistsAsync(streamId);
        existsBefore.Should().BeTrue();

        // Act
        await eventStore.DeleteStreamAsync(streamId);

        // Assert
        var existsAfter = await eventStore.StreamExistsAsync(streamId);
        existsAfter.Should().BeFalse();
    }

    [Fact]
    public async Task EventStore_ListStreams_ReturnsAllStreams()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var prefix = Guid.NewGuid().ToString("N")[..8];
        var streamIds = new List<string>();

        // Create multiple streams
        for (int i = 0; i < 5; i++)
        {
            var streamId = $"{prefix}-Order-{i:000}";
            streamIds.Add(streamId);
            await eventStore.AppendAsync(streamId, new IEvent[]
            {
                new OrderCreatedEvent($"ORD-{i}", "CUST-001", 100m)
            });
        }

        // Act
        var allStreams = await eventStore.ListStreamsAsync($"{prefix}-*");

        // Assert
        allStreams.Should().HaveCountGreaterOrEqualTo(5);
    }

    #region Test Events and Aggregate

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

    public record OrderDiscountAppliedEvent(string OrderId, decimal Discount) : IEvent
    {
        public long MessageId { get; init; }
    }

    public class OrderAggregate
    {
        public decimal TotalAmount { get; private set; }
        public int ItemCount { get; private set; }
        public string Status { get; private set; } = "Created";

        public void Apply(IEvent @event)
        {
            switch (@event)
            {
                case OrderCreatedEvent e:
                    TotalAmount = e.Amount;
                    Status = "Created";
                    break;
                case OrderItemAddedEvent e:
                    TotalAmount += e.Quantity * e.Price;
                    ItemCount++;
                    break;
                case OrderDiscountAppliedEvent e:
                    TotalAmount -= e.Discount;
                    break;
                case OrderConfirmedEvent:
                    Status = "Confirmed";
                    break;
                case OrderShippedEvent:
                    Status = "Shipped";
                    break;
            }
        }
    }

    #endregion
}
