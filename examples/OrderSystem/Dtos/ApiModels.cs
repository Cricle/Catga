using OrderSystem.Models;

namespace OrderSystem.Dtos;

public record CreateOrderRequest(string CustomerId, List<OrderItem> Items);

public record PayOrderRequest(string PaymentMethod);

public record ShipOrderRequest(string TrackingNumber);

public record HealthResponse(string Status, DateTime Timestamp);

public record SystemInfoResponse(
    string Service,
    string Version,
    string Node,
    string Mode,
    string Transport,
    string Persistence,
    string Status,
    DateTime Timestamp);

public record StatsResponse(
    int TotalOrders,
    Dictionary<string, int> ByStatus,
    decimal TotalRevenue,
    DateTime Timestamp);
