using System.Text.Json.Serialization;
using OrderSystem.Api.Domain;

namespace OrderSystem.Api;

[JsonSerializable(typeof(SystemInfoResponse))]
[JsonSerializable(typeof(Order))]
[JsonSerializable(typeof(OrderItem))]
[JsonSerializable(typeof(List<Order>))]
[JsonSerializable(typeof(List<OrderItem>))]
[JsonSerializable(typeof(CreateOrderRequest))]
[JsonSerializable(typeof(OrderCreatedResponse))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class AppJsonContext : JsonSerializerContext
{
}

public record CreateOrderRequest(string CustomerId, List<OrderItemRequest> Items);
public record OrderItemRequest(string ProductId, string ProductName, int Quantity, decimal UnitPrice);
public record OrderCreatedResponse(string OrderId, string Status);
