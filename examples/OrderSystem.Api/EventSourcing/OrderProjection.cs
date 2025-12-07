using Catga.Abstractions;
using Catga.EventSourcing;
using OrderSystem.Api.Domain;

namespace OrderSystem.Api.EventSourcing;

/// <summary>
/// Order summary projection - aggregates order data into a read model.
/// Demonstrates the projection feature.
/// </summary>
public class OrderSummaryProjection : IProjection
{
    public string Name => "OrderSummary";

    // Read model
    public Dictionary<string, OrderSummaryReadModel> Orders { get; } = new();
    public decimal TotalRevenue { get; private set; }
    public int TotalOrders { get; private set; }
    public Dictionary<string, int> OrdersByStatus { get; } = new();

    public ValueTask ApplyAsync(IEvent @event, CancellationToken ct = default)
    {
        switch (@event)
        {
            case OrderAggregateCreated e:
                Orders[e.OrderId] = new OrderSummaryReadModel
                {
                    OrderId = e.OrderId,
                    CustomerId = e.CustomerId,
                    TotalAmount = e.InitialAmount,
                    Status = "Created",
                    CreatedAt = e.Timestamp
                };
                TotalOrders++;
                IncrementStatus("Created");
                break;

            case OrderItemAdded e:
                if (Orders.TryGetValue(e.OrderId, out var order))
                {
                    order.TotalAmount += e.Price * e.Quantity;
                    order.ItemCount++;
                    TotalRevenue += e.Price * e.Quantity;
                }
                break;

            case OrderStatusChanged e:
                if (Orders.TryGetValue(e.OrderId, out var orderStatus))
                {
                    DecrementStatus(orderStatus.Status);
                    orderStatus.Status = e.NewStatus;
                    IncrementStatus(e.NewStatus);
                }
                break;

            case OrderDiscountApplied e:
                if (Orders.TryGetValue(e.OrderId, out var orderDiscount))
                {
                    orderDiscount.TotalAmount -= e.DiscountAmount;
                    TotalRevenue -= e.DiscountAmount;
                }
                break;
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask ResetAsync(CancellationToken ct = default)
    {
        Orders.Clear();
        TotalRevenue = 0;
        TotalOrders = 0;
        OrdersByStatus.Clear();
        return ValueTask.CompletedTask;
    }

    private void IncrementStatus(string status)
    {
        OrdersByStatus.TryGetValue(status, out var count);
        OrdersByStatus[status] = count + 1;
    }

    private void DecrementStatus(string status)
    {
        if (OrdersByStatus.TryGetValue(status, out var count) && count > 0)
            OrdersByStatus[status] = count - 1;
    }
}

public class OrderSummaryReadModel
{
    public string OrderId { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "";
    public int ItemCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Customer statistics projection.
/// </summary>
public class CustomerStatsProjection : IProjection
{
    public string Name => "CustomerStats";

    public Dictionary<string, CustomerStats> Stats { get; } = new();

    public ValueTask ApplyAsync(IEvent @event, CancellationToken ct = default)
    {
        switch (@event)
        {
            case OrderAggregateCreated e:
                if (!Stats.ContainsKey(e.CustomerId))
                    Stats[e.CustomerId] = new CustomerStats { CustomerId = e.CustomerId };
                Stats[e.CustomerId].OrderCount++;
                break;

            case OrderItemAdded e:
                // Find customer by order - simplified for demo
                break;

            case OrderStatusChanged e when e.NewStatus == "Confirmed":
                // Track confirmed orders
                break;
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask ResetAsync(CancellationToken ct = default)
    {
        Stats.Clear();
        return ValueTask.CompletedTask;
    }
}

public class CustomerStats
{
    public string CustomerId { get; set; } = "";
    public int OrderCount { get; set; }
    public decimal TotalSpent { get; set; }
}
