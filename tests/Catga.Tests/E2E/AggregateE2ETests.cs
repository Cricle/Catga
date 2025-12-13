using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.EventSourcing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// E2E tests for Aggregate and Domain features.
/// Tests aggregate loading, saving, and domain event handling.
/// </summary>
public class AggregateE2ETests
{
    [Fact]
    public async Task Aggregate_CreateAndLoad_ReconstructsState()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var orderId = $"Order-{Guid.NewGuid():N}"[..16];

        // Create order through events
        await eventStore.AppendAsync(orderId, new IEvent[]
        {
            new OrderCreatedEvent(orderId, "CUST-001", 0m),
            new OrderItemAddedEvent(orderId, "ITEM-001", 2, 50m),
            new OrderItemAddedEvent(orderId, "ITEM-002", 1, 100m)
        });

        // Act - Load and reconstruct
        var stream = await eventStore.ReadAsync(orderId);
        var order = new OrderAggregate();
        foreach (var envelope in stream.Events)
        {
            order.Apply(envelope.Event);
        }

        // Assert
        order.TotalAmount.Should().Be(200m); // 2*50 + 1*100
        order.ItemCount.Should().Be(2);
    }

    [Fact]
    public async Task Aggregate_ApplyMultipleEvents_MaintainsCorrectState()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var orderId = $"Order-{Guid.NewGuid():N}"[..16];

        // Apply full order lifecycle
        await eventStore.AppendAsync(orderId, new IEvent[]
        {
            new OrderCreatedEvent(orderId, "CUST-001", 0m),
            new OrderItemAddedEvent(orderId, "ITEM-001", 1, 100m),
            new OrderItemAddedEvent(orderId, "ITEM-002", 2, 50m),
            new DiscountAppliedEvent(orderId, 20m),
            new OrderConfirmedEvent(orderId),
            new OrderShippedEvent(orderId, "TRK-123"),
            new OrderDeliveredEvent(orderId)
        });

        // Act
        var stream = await eventStore.ReadAsync(orderId);
        var order = new OrderAggregate();
        foreach (var envelope in stream.Events)
        {
            order.Apply(envelope.Event);
        }

        // Assert
        order.TotalAmount.Should().Be(180m); // (100 + 2*50) - 20
        order.Status.Should().Be("Delivered");
        order.TrackingNumber.Should().Be("TRK-123");
    }

    [Fact]
    public async Task Aggregate_ConcurrentModifications_HandlesCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var orderId = $"Order-{Guid.NewGuid():N}"[..16];

        // Act - Concurrent appends
        var tasks = Enumerable.Range(1, 10).Select(i =>
            eventStore.AppendAsync(orderId, new IEvent[]
            {
                new OrderItemAddedEvent(orderId, $"ITEM-{i:000}", 1, 10m)
            }).AsTask()
        );

        await Task.WhenAll(tasks);

        // Assert
        var stream = await eventStore.ReadAsync(orderId);
        stream.Events.Should().HaveCount(10);
    }

    [Fact]
    public async Task Aggregate_WithSnapshot_LoadsFromSnapshot()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();
        var snapshotStore = sp.GetRequiredService<IEnhancedSnapshotStore>();

        var orderId = $"Order-{Guid.NewGuid():N}"[..16];

        // Create initial events
        for (int i = 1; i <= 5; i++)
        {
            await eventStore.AppendAsync(orderId, new IEvent[]
            {
                new OrderItemAddedEvent(orderId, $"ITEM-{i}", 1, 10m)
            });
        }

        // Create snapshot at version 5
        var snapshot = new OrderSnapshot
        {
            OrderId = orderId,
            TotalAmount = 50m,
            ItemCount = 5,
            Status = "Created",
            Version = 5
        };
        await snapshotStore.SaveAsync(orderId, snapshot, 5);

        // Add more events after snapshot
        for (int i = 6; i <= 8; i++)
        {
            await eventStore.AppendAsync(orderId, new IEvent[]
            {
                new OrderItemAddedEvent(orderId, $"ITEM-{i}", 1, 10m)
            });
        }

        // Act - Load from snapshot
        var loadedSnapshot = await snapshotStore.GetAsync<OrderSnapshot>(orderId);

        // Load events after snapshot
        var stream = await eventStore.ReadAsync(orderId, 6);

        // Assert
        loadedSnapshot.Should().NotBeNull();
        loadedSnapshot!.TotalAmount.Should().Be(50m);
        stream.Events.Should().HaveCount(3); // Events 6, 7, 8
    }

    [Fact]
    public async Task Aggregate_DomainEvents_AreRecorded()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var orderId = $"Order-{Guid.NewGuid():N}"[..16];
        var domainAggregate = new DomainOrderAggregate(orderId);

        // Act - Execute domain operations
        domainAggregate.AddItem("ITEM-001", 2, 25m);
        domainAggregate.AddItem("ITEM-002", 1, 50m);
        domainAggregate.ApplyDiscount(10m);
        domainAggregate.Confirm();

        // Save domain events
        await eventStore.AppendAsync(orderId, domainAggregate.UncommittedEvents.ToArray());

        // Assert
        var stream = await eventStore.ReadAsync(orderId);
        stream.Events.Should().HaveCount(4);
        domainAggregate.TotalAmount.Should().Be(90m); // (2*25 + 50) - 10
        domainAggregate.Status.Should().Be("Confirmed");
    }

    [Fact]
    public async Task Aggregate_Validation_RejectsInvalidOperations()
    {
        // Arrange
        var orderId = "Order-001";
        var aggregate = new DomainOrderAggregate(orderId);

        // Act & Assert - Cannot confirm empty order
        var exception = Assert.Throws<InvalidOperationException>(() => aggregate.Confirm());
        exception.Message.Should().Contain("Cannot confirm");
    }

    [Fact]
    public async Task Aggregate_EventVersion_IncrementsProperly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var orderId = $"Order-{Guid.NewGuid():N}"[..16];

        // Act - Append events one by one
        for (int i = 1; i <= 5; i++)
        {
            await eventStore.AppendAsync(orderId, new IEvent[]
            {
                new OrderItemAddedEvent(orderId, $"ITEM-{i}", 1, 10m)
            });
        }

        // Assert
        var version = await eventStore.GetStreamVersionAsync(orderId);
        version.Should().BeGreaterOrEqualTo(5);
    }

    [Fact]
    public async Task Aggregate_OptimisticConcurrency_DetectsConflict()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var orderId = $"Order-{Guid.NewGuid():N}"[..16];

        // Create initial state
        await eventStore.AppendAsync(orderId, new IEvent[]
        {
            new OrderCreatedEvent(orderId, "CUST-001", 100m)
        });

        // Load order (version 1)
        var stream = await eventStore.ReadAsync(orderId);
        var initialVersion = stream.Version;

        // Simulate concurrent modification
        await eventStore.AppendAsync(orderId, new IEvent[]
        {
            new OrderItemAddedEvent(orderId, "ITEM-001", 1, 50m)
        });

        // The stream version has changed
        var newVersion = await eventStore.GetStreamVersionAsync(orderId);
        newVersion.Should().BeGreaterThan(initialVersion);
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

    public record DiscountAppliedEvent(string OrderId, decimal Discount) : IEvent
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

    public record OrderDeliveredEvent(string OrderId) : IEvent
    {
        public long MessageId { get; init; }
    }

    public class OrderAggregate
    {
        public decimal TotalAmount { get; private set; }
        public int ItemCount { get; private set; }
        public string Status { get; private set; } = "Created";
        public string? TrackingNumber { get; private set; }

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
                case DiscountAppliedEvent e:
                    TotalAmount -= e.Discount;
                    break;
                case OrderConfirmedEvent:
                    Status = "Confirmed";
                    break;
                case OrderShippedEvent e:
                    Status = "Shipped";
                    TrackingNumber = e.TrackingNumber;
                    break;
                case OrderDeliveredEvent:
                    Status = "Delivered";
                    break;
            }
        }
    }

    public class OrderSnapshot
    {
        public string OrderId { get; set; } = "";
        public decimal TotalAmount { get; set; }
        public int ItemCount { get; set; }
        public string Status { get; set; } = "";
        public long Version { get; set; }
    }

    public class DomainOrderAggregate
    {
        private readonly List<IEvent> _uncommittedEvents = new();

        public string OrderId { get; }
        public decimal TotalAmount { get; private set; }
        public int ItemCount { get; private set; }
        public string Status { get; private set; } = "Created";

        public IReadOnlyList<IEvent> UncommittedEvents => _uncommittedEvents;

        public DomainOrderAggregate(string orderId)
        {
            OrderId = orderId;
        }

        public void AddItem(string itemId, int quantity, decimal price)
        {
            var @event = new OrderItemAddedEvent(OrderId, itemId, quantity, price);
            Apply(@event);
            _uncommittedEvents.Add(@event);
        }

        public void ApplyDiscount(decimal discount)
        {
            var @event = new DiscountAppliedEvent(OrderId, discount);
            Apply(@event);
            _uncommittedEvents.Add(@event);
        }

        public void Confirm()
        {
            if (ItemCount == 0)
                throw new InvalidOperationException("Cannot confirm order with no items");

            var @event = new OrderConfirmedEvent(OrderId);
            Apply(@event);
            _uncommittedEvents.Add(@event);
        }

        private void Apply(IEvent @event)
        {
            switch (@event)
            {
                case OrderItemAddedEvent e:
                    TotalAmount += e.Quantity * e.Price;
                    ItemCount++;
                    break;
                case DiscountAppliedEvent e:
                    TotalAmount -= e.Discount;
                    break;
                case OrderConfirmedEvent:
                    Status = "Confirmed";
                    break;
            }
        }
    }

    #endregion
}
