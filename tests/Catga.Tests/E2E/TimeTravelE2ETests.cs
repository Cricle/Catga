using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.EventSourcing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// E2E tests for Time Travel functionality.
/// Tests state reconstruction at any point in time.
/// </summary>
public class TimeTravelE2ETests
{
    [Fact]
    public async Task TimeTravel_GetStateAtVersion_ReconstructsCorrectState()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();
        services.AddTimeTravelService<TestAggregate>();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();
        var timeTravel = sp.GetRequiredService<ITimeTravelService<TestAggregate>>();

        var aggregateId = $"agg-{Guid.NewGuid():N}"[..12];
        var streamId = $"TestAggregate-{aggregateId}";

        // Append events
        await eventStore.AppendAsync(streamId, new IEvent[]
        {
            new AggregateCreated { AggregateId = aggregateId, Name = "Initial" },
            new AggregateUpdated { AggregateId = aggregateId, Name = "Updated1", Value = 100 },
            new AggregateUpdated { AggregateId = aggregateId, Name = "Updated2", Value = 200 },
            new AggregateUpdated { AggregateId = aggregateId, Name = "Updated3", Value = 300 }
        });

        // Act - Get state at version 1
        var stateV1 = await timeTravel.GetStateAtVersionAsync(aggregateId, 1);
        var stateV2 = await timeTravel.GetStateAtVersionAsync(aggregateId, 2);
        var stateV3 = await timeTravel.GetStateAtVersionAsync(aggregateId, 3);
        var stateV4 = await timeTravel.GetStateAtVersionAsync(aggregateId, 4);

        // Assert
        stateV1.Should().NotBeNull();
        stateV1!.Name.Should().Be("Initial");
        stateV1.Value.Should().Be(0);

        stateV2.Should().NotBeNull();
        stateV2!.Name.Should().Be("Updated1");
        stateV2.Value.Should().Be(100);

        stateV3.Should().NotBeNull();
        stateV3!.Name.Should().Be("Updated2");
        stateV3.Value.Should().Be(200);

        stateV4.Should().NotBeNull();
        stateV4!.Name.Should().Be("Updated3");
        stateV4.Value.Should().Be(300);
    }

    [Fact]
    public async Task TimeTravel_GetVersionHistory_ReturnsAllVersions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();
        services.AddTimeTravelService<TestAggregate>();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();
        var timeTravel = sp.GetRequiredService<ITimeTravelService<TestAggregate>>();

        var aggregateId = $"agg-{Guid.NewGuid():N}"[..12];
        var streamId = $"TestAggregate-{aggregateId}";

        await eventStore.AppendAsync(streamId, new IEvent[]
        {
            new AggregateCreated { AggregateId = aggregateId, Name = "Test" },
            new AggregateUpdated { AggregateId = aggregateId, Name = "V2", Value = 10 },
            new AggregateUpdated { AggregateId = aggregateId, Name = "V3", Value = 20 }
        });

        // Act
        var history = await timeTravel.GetVersionHistoryAsync(aggregateId);

        // Assert
        history.Should().HaveCount(3);
        history[0].Version.Should().Be(1);
        history[0].EventType.Should().Contain("AggregateCreated");
        history[1].Version.Should().Be(2);
        history[2].Version.Should().Be(3);
    }

    [Fact]
    public async Task TimeTravel_NonExistentAggregate_ReturnsNull()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();
        services.AddTimeTravelService<TestAggregate>();

        var sp = services.BuildServiceProvider();
        var timeTravel = sp.GetRequiredService<ITimeTravelService<TestAggregate>>();

        // Act
        var state = await timeTravel.GetStateAtVersionAsync("non-existent", 1);

        // Assert
        state.Should().BeNull();
    }

    [Fact]
    public async Task TimeTravel_VersionBeyondCurrent_ReturnsLatestState()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();
        services.AddTimeTravelService<TestAggregate>();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();
        var timeTravel = sp.GetRequiredService<ITimeTravelService<TestAggregate>>();

        var aggregateId = $"agg-{Guid.NewGuid():N}"[..12];
        var streamId = $"TestAggregate-{aggregateId}";

        await eventStore.AppendAsync(streamId, new IEvent[]
        {
            new AggregateCreated { AggregateId = aggregateId, Name = "Test" },
            new AggregateUpdated { AggregateId = aggregateId, Name = "Final", Value = 999 }
        });

        // Act - Request version 100 (only 2 events exist)
        var state = await timeTravel.GetStateAtVersionAsync(aggregateId, 100);

        // Assert - Should return state at version 2 (latest)
        state.Should().NotBeNull();
        state!.Name.Should().Be("Final");
        state.Value.Should().Be(999);
    }

    [Fact]
    public async Task TimeTravel_WithComplexAggregate_MaintainsCollections()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();
        services.AddTimeTravelService<OrderAggregate>();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();
        var timeTravel = sp.GetRequiredService<ITimeTravelService<OrderAggregate>>();

        var orderId = $"ord-{Guid.NewGuid():N}"[..12];
        var streamId = $"OrderAggregate-{orderId}";

        await eventStore.AppendAsync(streamId, new IEvent[]
        {
            new OrderCreated { OrderId = orderId, CustomerId = "CUST-001" },
            new ItemAdded { OrderId = orderId, Sku = "SKU-001", Quantity = 2, Price = 10m },
            new ItemAdded { OrderId = orderId, Sku = "SKU-002", Quantity = 1, Price = 50m },
            new ItemRemoved { OrderId = orderId, Sku = "SKU-001" }
        });

        // Act
        var stateV2 = await timeTravel.GetStateAtVersionAsync(orderId, 2);
        var stateV3 = await timeTravel.GetStateAtVersionAsync(orderId, 3);
        var stateV4 = await timeTravel.GetStateAtVersionAsync(orderId, 4);

        // Assert
        stateV2!.Items.Should().HaveCount(1);
        stateV2.Items.Should().ContainKey("SKU-001");

        stateV3!.Items.Should().HaveCount(2);
        stateV3.TotalAmount.Should().Be(70m);

        stateV4!.Items.Should().HaveCount(1);
        stateV4.Items.Should().ContainKey("SKU-002");
        stateV4.TotalAmount.Should().Be(50m);
    }

    #region Test Aggregates

    public class TestAggregate : IAggregateRoot
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Value { get; set; }
        public long Version { get; set; }

        public void Apply(IEvent @event)
        {
            switch (@event)
            {
                case AggregateCreated e:
                    Id = e.AggregateId;
                    Name = e.Name;
                    break;
                case AggregateUpdated e:
                    Name = e.Name;
                    Value = e.Value;
                    break;
            }
            Version++;
        }
    }

    public class OrderAggregate : IAggregateRoot
    {
        public string Id { get; set; } = "";
        public string CustomerId { get; set; } = "";
        public Dictionary<string, OrderItem> Items { get; set; } = new();
        public decimal TotalAmount { get; set; }
        public long Version { get; set; }

        public void Apply(IEvent @event)
        {
            switch (@event)
            {
                case OrderCreated e:
                    Id = e.OrderId;
                    CustomerId = e.CustomerId;
                    break;
                case ItemAdded e:
                    Items[e.Sku] = new OrderItem(e.Sku, e.Quantity, e.Price);
                    TotalAmount = Items.Values.Sum(i => i.Quantity * i.Price);
                    break;
                case ItemRemoved e:
                    Items.Remove(e.Sku);
                    TotalAmount = Items.Values.Sum(i => i.Quantity * i.Price);
                    break;
            }
            Version++;
        }
    }

    public record OrderItem(string Sku, int Quantity, decimal Price);

    #endregion

    #region Test Events

    public record AggregateCreated : IEvent
    {
        public long MessageId { get; init; }
        public string AggregateId { get; init; } = "";
        public string Name { get; init; } = "";
    }

    public record AggregateUpdated : IEvent
    {
        public long MessageId { get; init; }
        public string AggregateId { get; init; } = "";
        public string Name { get; init; } = "";
        public int Value { get; init; }
    }

    public record OrderCreated : IEvent
    {
        public long MessageId { get; init; }
        public string OrderId { get; init; } = "";
        public string CustomerId { get; init; } = "";
    }

    public record ItemAdded : IEvent
    {
        public long MessageId { get; init; }
        public string OrderId { get; init; } = "";
        public string Sku { get; init; } = "";
        public int Quantity { get; init; }
        public decimal Price { get; init; }
    }

    public record ItemRemoved : IEvent
    {
        public long MessageId { get; init; }
        public string OrderId { get; init; } = "";
        public string Sku { get; init; } = "";
    }

    #endregion
}
