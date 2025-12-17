using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
#if !AOT_MINIMAL
using OrderSystem.Api.Domain;
using OrderSystem.Api.Endpoints;
using OrderSystem.Api.Messages;
#endif

namespace OrderSystem.Api;

// JSON Source Generation for AOT compatibility
[JsonSerializable(typeof(SystemInfoResponse))]
[JsonSerializable(typeof(ProblemDetails))]
#if !AOT_MINIMAL
[JsonSerializable(typeof(Order))]
[JsonSerializable(typeof(OrderItem))]
[JsonSerializable(typeof(ShippingAddress))]
[JsonSerializable(typeof(List<Order>))]
[JsonSerializable(typeof(List<OrderItem>))]
[JsonSerializable(typeof(OrderStats))]
[JsonSerializable(typeof(OrderCreatedResult))]
// Request DTOs
[JsonSerializable(typeof(CancelOrderRequest))]
[JsonSerializable(typeof(PayOrderRequest))]
[JsonSerializable(typeof(ShipOrderRequest))]
[JsonSerializable(typeof(CreateOrderCommand))]
[JsonSerializable(typeof(CreateOrderFlowCommand))]
// Response types
[JsonSerializable(typeof(SuccessResponse))]
[JsonSerializable(typeof(MessageResponse))]
[JsonSerializable(typeof(ShipResponse))]
#endif
// Collections
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class AppJsonContext : JsonSerializerContext
{
}

// AOT-compatible response types (replacing anonymous types)
public record SuccessResponse(string Message, string OrderId);
public record MessageResponse(string Message);
public record ShipResponse(string Message, string OrderId, string TrackingNumber);
