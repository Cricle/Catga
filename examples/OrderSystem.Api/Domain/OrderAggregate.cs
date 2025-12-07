using Catga.Abstractions;
using Catga.EventSourcing;
using MemoryPack;

namespace OrderSystem.Api.Domain;

/// <summary>
/// Event-sourced Order aggregate for time travel demo.
/// </summary>
public class OrderAggregate : AggregateRoot
{
    public override string Id { get; protected set; } = string.Empty;
    public string CustomerId { get; private set; } = string.Empty;
    public List<OrderItemData> Items { get; private set; } = [];
    public decimal TotalAmount { get; private set; }
    public string Status { get; private set; } = "Created";
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    protected override void When(IEvent @event)
    {
        switch (@event)
        {
            case OrderAggregateCreated e:
                Id = e.OrderId;
                CustomerId = e.CustomerId;
                TotalAmount = e.InitialAmount;
                Status = "Created";
                CreatedAt = e.Timestamp;
                break;
            case OrderItemAdded e:
                Items.Add(new OrderItemData { ProductName = e.ProductName, Quantity = e.Quantity, Price = e.Price });
                TotalAmount += e.Price * e.Quantity;
                UpdatedAt = e.Timestamp;
                break;
            case OrderItemRemoved e:
                var item = Items.FirstOrDefault(i => i.ProductName == e.ProductName);
                if (item != null)
                {
                    TotalAmount -= item.Price * item.Quantity;
                    Items.Remove(item);
                }
                UpdatedAt = e.Timestamp;
                break;
            case OrderStatusChanged e:
                Status = e.NewStatus;
                UpdatedAt = e.Timestamp;
                break;
            case OrderDiscountApplied e:
                TotalAmount -= e.DiscountAmount;
                if (TotalAmount < 0) TotalAmount = 0;
                UpdatedAt = e.Timestamp;
                break;
        }
    }
}

public class OrderItemData
{
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

#region Events

[MemoryPackable]
public partial record OrderAggregateCreated : IEvent
{
    public long MessageId { get; init; }
    public long? CorrelationId { get; init; }
    public long? CausationId { get; init; }
    public required string OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required decimal InitialAmount { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

[MemoryPackable]
public partial record OrderItemAdded : IEvent
{
    public long MessageId { get; init; }
    public long? CorrelationId { get; init; }
    public long? CausationId { get; init; }
    public required string OrderId { get; init; }
    public required string ProductName { get; init; }
    public required int Quantity { get; init; }
    public required decimal Price { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

[MemoryPackable]
public partial record OrderItemRemoved : IEvent
{
    public long MessageId { get; init; }
    public long? CorrelationId { get; init; }
    public long? CausationId { get; init; }
    public required string OrderId { get; init; }
    public required string ProductName { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

[MemoryPackable]
public partial record OrderStatusChanged : IEvent
{
    public long MessageId { get; init; }
    public long? CorrelationId { get; init; }
    public long? CausationId { get; init; }
    public required string OrderId { get; init; }
    public required string NewStatus { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

[MemoryPackable]
public partial record OrderDiscountApplied : IEvent
{
    public long MessageId { get; init; }
    public long? CorrelationId { get; init; }
    public long? CausationId { get; init; }
    public required string OrderId { get; init; }
    public required decimal DiscountAmount { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

#endregion
