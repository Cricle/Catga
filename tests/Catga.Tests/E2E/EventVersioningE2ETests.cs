using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.EventSourcing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// E2E tests for Event Versioning and Schema Evolution.
/// Tests upgrading events from old versions to new versions.
/// </summary>
public class EventVersioningE2ETests
{
    [Fact]
    public void EventVersionRegistry_RegisterAndResolve_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        // Register event upgrader
        var registry = new EventVersionRegistry();
        registry.Register(new OrderItemAddedV1ToV2Upgrader());
        services.AddSingleton<IEventVersionRegistry>(registry);

        var sp = services.BuildServiceProvider();
        var versionRegistry = sp.GetRequiredService<IEventVersionRegistry>();

        // Act
        var upgrader = versionRegistry.GetUpgrader(typeof(OrderItemAddedV1));

        // Assert
        upgrader.Should().NotBeNull();
        upgrader!.SourceVersion.Should().Be(1);
        upgrader.TargetVersion.Should().Be(2);
    }

    [Fact]
    public void EventUpgrader_UpgradesEventCorrectly()
    {
        // Arrange
        var upgrader = new OrderItemAddedV1ToV2Upgrader();
        var v1Event = new OrderItemAddedV1
        {
            MessageId = 1,
            OrderId = "ORD-001",
            ProductName = "Laptop",
            Quantity = 2,
            Price = 999.99m
        };

        // Act
        var v2Event = upgrader.Upgrade(v1Event) as OrderItemAddedV2;

        // Assert
        v2Event.Should().NotBeNull();
        v2Event!.OrderId.Should().Be("ORD-001");
        v2Event.ProductName.Should().Be("Laptop");
        v2Event.Sku.Should().Be("SKU-LAPTOP"); // Auto-generated from product name
        v2Event.Quantity.Should().Be(2);
        v2Event.Price.Should().Be(999.99m);
    }

    [Fact]
    public void EventTypeRegistry_RegisterAndResolve_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var typeRegistry = sp.GetRequiredService<IEventTypeRegistry>();

        // Act
        typeRegistry.Register<OrderCreatedEvent>("OrderCreated");
        typeRegistry.Register<OrderUpdatedEvent>("OrderUpdated");

        var createdType = typeRegistry.Resolve("OrderCreated");
        var updatedType = typeRegistry.Resolve("OrderUpdated");

        // Assert
        createdType.Should().Be(typeof(OrderCreatedEvent));
        updatedType.Should().Be(typeof(OrderUpdatedEvent));
    }

    [Fact]
    public void EventTypeRegistry_UnknownType_ReturnsNull()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var typeRegistry = sp.GetRequiredService<IEventTypeRegistry>();

        // Act
        var unknownType = typeRegistry.Resolve("UnknownEventType");

        // Assert
        unknownType.Should().BeNull();
    }

    [Fact]
    public void EventVersionRegistry_ChainedUpgraders_UpgradesMultipleVersions()
    {
        // Arrange
        var registry = new EventVersionRegistry();
        registry.Register(new ProductEventV1ToV2Upgrader());
        registry.Register(new ProductEventV2ToV3Upgrader());

        var v1Event = new ProductEventV1 { ProductId = "P001", Name = "Widget" };

        // Act - Upgrade from V1 to V2
        var v1Upgrader = registry.GetUpgrader(typeof(ProductEventV1));
        var v2Event = v1Upgrader!.Upgrade(v1Event) as ProductEventV2;

        // Act - Upgrade from V2 to V3
        var v2Upgrader = registry.GetUpgrader(typeof(ProductEventV2));
        var v3Event = v2Upgrader!.Upgrade(v2Event!) as ProductEventV3;

        // Assert
        v3Event.Should().NotBeNull();
        v3Event!.ProductId.Should().Be("P001");
        v3Event.Name.Should().Be("Widget");
        v3Event.Sku.Should().Be("SKU-P001");
        v3Event.Category.Should().Be("Default");
    }

    [Fact]
    public async Task EventStore_WithVersioning_StoresAndRetrievesEvents()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var streamId = $"product-{Guid.NewGuid():N}";
        var events = new IEvent[]
        {
            new ProductEventV3 { ProductId = "P001", Name = "Widget", Sku = "SKU-001", Category = "Electronics" },
            new ProductEventV3 { ProductId = "P001", Name = "Widget Pro", Sku = "SKU-001", Category = "Electronics" }
        };

        // Act
        await eventStore.AppendAsync(streamId, events);
        var stream = await eventStore.ReadAsync(streamId);

        // Assert
        stream.Events.Should().HaveCount(2);
        var firstEvent = stream.Events[0].Event as ProductEventV3;
        firstEvent!.Name.Should().Be("Widget");
        firstEvent.Category.Should().Be("Electronics");
    }

    #region Test Events

    public record OrderItemAddedV1 : IEvent
    {
        public long MessageId { get; init; }
        public string OrderId { get; init; } = "";
        public string ProductName { get; init; } = "";
        public int Quantity { get; init; }
        public decimal Price { get; init; }
    }

    public record OrderItemAddedV2 : IEvent
    {
        public long MessageId { get; init; }
        public string OrderId { get; init; } = "";
        public string ProductName { get; init; } = "";
        public string Sku { get; init; } = "";
        public int Quantity { get; init; }
        public decimal Price { get; init; }
    }

    public record OrderCreatedEvent : IEvent
    {
        public long MessageId { get; init; }
        public string OrderId { get; init; } = "";
    }

    public record OrderUpdatedEvent : IEvent
    {
        public long MessageId { get; init; }
        public string OrderId { get; init; } = "";
    }

    public record ProductEventV1 : IEvent
    {
        public long MessageId { get; init; }
        public string ProductId { get; init; } = "";
        public string Name { get; init; } = "";
    }

    public record ProductEventV2 : IEvent
    {
        public long MessageId { get; init; }
        public string ProductId { get; init; } = "";
        public string Name { get; init; } = "";
        public string Sku { get; init; } = "";
    }

    public record ProductEventV3 : IEvent
    {
        public long MessageId { get; init; }
        public string ProductId { get; init; } = "";
        public string Name { get; init; } = "";
        public string Sku { get; init; } = "";
        public string Category { get; init; } = "";
    }

    #endregion

    #region Upgraders

    public class OrderItemAddedV1ToV2Upgrader : EventUpgrader<OrderItemAddedV1, OrderItemAddedV2>
    {
        public override int SourceVersion => 1;
        public override int TargetVersion => 2;

        protected override OrderItemAddedV2 UpgradeCore(OrderItemAddedV1 source) => new()
        {
            MessageId = source.MessageId,
            OrderId = source.OrderId,
            ProductName = source.ProductName,
            Sku = $"SKU-{source.ProductName.ToUpperInvariant().Replace(" ", "-")}",
            Quantity = source.Quantity,
            Price = source.Price
        };
    }

    public class ProductEventV1ToV2Upgrader : EventUpgrader<ProductEventV1, ProductEventV2>
    {
        public override int SourceVersion => 1;
        public override int TargetVersion => 2;

        protected override ProductEventV2 UpgradeCore(ProductEventV1 source) => new()
        {
            MessageId = source.MessageId,
            ProductId = source.ProductId,
            Name = source.Name,
            Sku = $"SKU-{source.ProductId}"
        };
    }

    public class ProductEventV2ToV3Upgrader : EventUpgrader<ProductEventV2, ProductEventV3>
    {
        public override int SourceVersion => 2;
        public override int TargetVersion => 3;

        protected override ProductEventV3 UpgradeCore(ProductEventV2 source) => new()
        {
            MessageId = source.MessageId,
            ProductId = source.ProductId,
            Name = source.Name,
            Sku = source.Sku,
            Category = "Default"
        };
    }

    #endregion
}
