using Catga.Abstractions;
using Catga.EventSourcing;
using MemoryPack;
using OrderSystem.Api.Domain;

namespace OrderSystem.Api.EventSourcing;

/// <summary>
/// Demonstrates event versioning with upcasters.
/// When event schema changes, upcasters transform old events to new format.
/// </summary>

// V1: Original event (deprecated)
[MemoryPackable]
[EventVersion(1)]
public partial record OrderItemAddedV1 : IEvent
{
    public long MessageId { get; init; }
    public required string OrderId { get; init; }
    public required string ProductName { get; init; }
    public required int Quantity { get; init; }
    public required decimal Price { get; init; }
}

// V2: Added SKU field
[MemoryPackable]
[EventVersion(2)]
public partial record OrderItemAddedV2 : IEvent
{
    public long MessageId { get; init; }
    public required string OrderId { get; init; }
    public required string ProductName { get; init; }
    public required string Sku { get; init; }
    public required int Quantity { get; init; }
    public required decimal Price { get; init; }
}

/// <summary>
/// Upcaster from V1 to V2 - adds default SKU.
/// </summary>
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

/// <summary>
/// Extension to register event version upgraders.
/// </summary>
public static class EventVersioningExtensions
{
    public static IServiceCollection AddOrderEventVersioning(this IServiceCollection services)
    {
        services.AddSingleton<IEventVersionRegistry>(sp =>
        {
            var registry = new EventVersionRegistry();
            registry.Register(new OrderItemAddedV1ToV2Upgrader());
            return registry;
        });
        return services;
    }
}
