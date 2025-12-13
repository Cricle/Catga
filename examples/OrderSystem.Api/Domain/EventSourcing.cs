using Catga.Abstractions;
using Catga.EventSourcing;
using MemoryPack;
using Microsoft.Extensions.Logging;
using OrderSystem.Api.Domain;

namespace OrderSystem.Api.Domain;

// ============================================
// Projections (Read Models from Events)
// ============================================

/// <summary>Order summary projection - aggregates order data into a read model.</summary>
public class OrderSummaryProjection : IProjection
{
    public string Name => "OrderSummary";
    public Dictionary<string, OrderSummaryReadModel> Orders { get; } = new();
    public decimal TotalRevenue { get; private set; }
    public int TotalOrders { get; private set; }
    public Dictionary<string, int> OrdersByStatus { get; } = new();

    public ValueTask ApplyAsync(IEvent @event, CancellationToken ct = default)
    {
        switch (@event)
        {
            case OrderAggregateCreated e:
                Orders[e.OrderId] = new OrderSummaryReadModel { OrderId = e.OrderId, CustomerId = e.CustomerId, TotalAmount = e.InitialAmount, Status = "Created", CreatedAt = e.Timestamp };
                TotalOrders++;
                IncrementStatus("Created");
                break;
            case OrderItemAdded e:
                if (Orders.TryGetValue(e.OrderId, out var order)) { order.TotalAmount += e.Price * e.Quantity; order.ItemCount++; TotalRevenue += e.Price * e.Quantity; }
                break;
            case OrderStatusChanged e:
                if (Orders.TryGetValue(e.OrderId, out var o)) { DecrementStatus(o.Status); o.Status = e.NewStatus; IncrementStatus(e.NewStatus); }
                break;
            case OrderDiscountApplied e:
                if (Orders.TryGetValue(e.OrderId, out var od)) { od.TotalAmount -= e.DiscountAmount; TotalRevenue -= e.DiscountAmount; }
                break;
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask ResetAsync(CancellationToken ct = default) { Orders.Clear(); TotalRevenue = 0; TotalOrders = 0; OrdersByStatus.Clear(); return ValueTask.CompletedTask; }
    private void IncrementStatus(string s) { OrdersByStatus.TryGetValue(s, out var c); OrdersByStatus[s] = c + 1; }
    private void DecrementStatus(string s) { if (OrdersByStatus.TryGetValue(s, out var c) && c > 0) OrdersByStatus[s] = c - 1; }
}

public class OrderSummaryReadModel { public string OrderId { get; set; } = ""; public string CustomerId { get; set; } = ""; public decimal TotalAmount { get; set; } public string Status { get; set; } = ""; public int ItemCount { get; set; } public DateTime CreatedAt { get; set; } }

/// <summary>Customer statistics projection.</summary>
public class CustomerStatsProjection : IProjection
{
    public string Name => "CustomerStats";
    public Dictionary<string, CustomerStats> Stats { get; } = new();

    public ValueTask ApplyAsync(IEvent @event, CancellationToken ct = default)
    {
        if (@event is OrderAggregateCreated e)
        {
            if (!Stats.ContainsKey(e.CustomerId)) Stats[e.CustomerId] = new CustomerStats { CustomerId = e.CustomerId };
            Stats[e.CustomerId].OrderCount++;
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask ResetAsync(CancellationToken ct = default) { Stats.Clear(); return ValueTask.CompletedTask; }
}

public class CustomerStats { public string CustomerId { get; set; } = ""; public int OrderCount { get; set; } public decimal TotalSpent { get; set; } }

// ============================================
// Subscriptions (Persistent Event Handlers)
// ============================================

/// <summary>Order event handler for subscriptions.</summary>
public class OrderEventSubscriptionHandler : IEventHandler
{
    private readonly ILogger<OrderEventSubscriptionHandler> _logger;
    public OrderEventSubscriptionHandler(ILogger<OrderEventSubscriptionHandler> logger) => _logger = logger;

    public ValueTask HandleAsync(IEvent @event, CancellationToken ct = default)
    {
        switch (@event)
        {
            case OrderAggregateCreated e: _logger.LogInformation("[Sub] Order created: {OrderId}", e.OrderId); break;
            case OrderItemAdded e: _logger.LogInformation("[Sub] Item added: {ProductName} x{Qty}", e.ProductName, e.Quantity); break;
            case OrderStatusChanged e: _logger.LogInformation("[Sub] Status changed: {Status}", e.NewStatus); break;
        }
        return ValueTask.CompletedTask;
    }
}

// ============================================
// Audit & Compliance
// ============================================

/// <summary>Order audit service for compliance and GDPR.</summary>
public class OrderAuditService
{
    private readonly IAuditLogStore _auditStore;
    private readonly ImmutabilityVerifier _verifier;
    private readonly GdprService _gdprService;

    public OrderAuditService(IAuditLogStore auditStore, IEventStore eventStore, IGdprStore gdprStore)
    {
        _auditStore = auditStore;
        _verifier = new ImmutabilityVerifier(eventStore);
        _gdprService = new GdprService(gdprStore);
    }

    public ValueTask<VerificationResult> VerifyStreamAsync(string streamId) => _verifier.VerifyStreamAsync(streamId);
    public ValueTask<IReadOnlyList<AuditLogEntry>> GetLogsAsync(string streamId) => _auditStore.GetLogsAsync(streamId);
    public ValueTask RequestCustomerErasureAsync(string customerId, string requestedBy) => _gdprService.RequestErasureAsync(customerId, requestedBy);
    public ValueTask<IReadOnlyList<ErasureRequest>> GetPendingErasureRequestsAsync() => _gdprService.GetPendingRequestsAsync();
}

// ============================================
// Event Versioning (Schema Evolution)
// ============================================

[MemoryPackable, EventVersion(1)]
public partial record OrderItemAddedV1 : IEvent { public long MessageId { get; init; } public required string OrderId { get; init; } public required string ProductName { get; init; } public required int Quantity { get; init; } public required decimal Price { get; init; } }

[MemoryPackable, EventVersion(2)]
public partial record OrderItemAddedV2 : IEvent { public long MessageId { get; init; } public required string OrderId { get; init; } public required string ProductName { get; init; } public required string Sku { get; init; } public required int Quantity { get; init; } public required decimal Price { get; init; } }

/// <summary>Upcaster from V1 to V2 - adds default SKU.</summary>
public class OrderItemAddedV1ToV2Upgrader : EventUpgrader<OrderItemAddedV1, OrderItemAddedV2>
{
    public override int SourceVersion => 1;
    public override int TargetVersion => 2;
    protected override OrderItemAddedV2 UpgradeCore(OrderItemAddedV1 s) => new() { MessageId = s.MessageId, OrderId = s.OrderId, ProductName = s.ProductName, Sku = $"SKU-{s.ProductName.ToUpperInvariant().Replace(" ", "-")}", Quantity = s.Quantity, Price = s.Price };
}

/// <summary>
/// Extension methods for OrderSystem DI registration.
/// </summary>
public static class OrderSystemExtensions
{
    /// <summary>
    /// Adds all OrderSystem services including Event Versioning, Projections, and Audit services.
    /// </summary>
    public static IServiceCollection AddOrderSystem(this IServiceCollection services)
    {
        // Event versioning (schema evolution)
        services.AddSingleton<IEventVersionRegistry>(sp =>
        {
            var r = new EventVersionRegistry();
            r.Register(new OrderItemAddedV1ToV2Upgrader());
            return r;
        });

        // Projections (auto-registered by source generator, but explicit registration ensures availability)
        services.AddSingleton<OrderSummaryProjection>();
        services.AddSingleton<CustomerStatsProjection>();

        // Subscription handler
        services.AddSingleton<OrderEventSubscriptionHandler>();

        // Audit service
        services.AddSingleton<OrderAuditService>();

        return services;
    }

    /// <summary>
    /// Adds Order Event Versioning (schema evolution) services.
    /// </summary>
    public static IServiceCollection AddOrderEventVersioning(this IServiceCollection services)
    {
        services.AddSingleton<IEventVersionRegistry>(sp =>
        {
            var r = new EventVersionRegistry();
            r.Register(new OrderItemAddedV1ToV2Upgrader());
            return r;
        });
        return services;
    }
}
