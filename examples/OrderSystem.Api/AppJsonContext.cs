using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Endpoints;
using OrderSystem.Api.Messages;
using static OrderSystem.Api.Endpoints.AuthEndpoints;
using static OrderSystem.Api.Endpoints.EventSourcingEndpoints;
using static OrderSystem.Api.Endpoints.PaymentEndpoints;

namespace OrderSystem.Api;

// JSON Source Generation for AOT compatibility
[JsonSerializable(typeof(SystemInfoResponse))]
[JsonSerializable(typeof(ProblemDetails))]
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
// Auth endpoints
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(RefreshTokenRequest))]
// Payment endpoints
[JsonSerializable(typeof(PaymentRequest))]
[JsonSerializable(typeof(RefundRequest))]
// Event sourcing endpoints
[JsonSerializable(typeof(CreateSubscriptionRequest))]
[JsonSerializable(typeof(GdprErasureRequest))]
// AOT response types
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(FlowInfoResponse))]
[JsonSerializable(typeof(FlowRegisteredResponse))]
[JsonSerializable(typeof(FlowReloadedResponse))]
[JsonSerializable(typeof(EventCreatedResponse))]
[JsonSerializable(typeof(ProjectionRebuildResponse))]
[JsonSerializable(typeof(SubscriptionCreatedResponse))]
[JsonSerializable(typeof(SubscriptionProcessedResponse))]
[JsonSerializable(typeof(StreamVerifyResponse))]
[JsonSerializable(typeof(SnapshotCreatedResponse))]
[JsonSerializable(typeof(UserRegisteredResponse))]
[JsonSerializable(typeof(CurrentUserResponse))]
// Collections
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(object))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class AppJsonContext : JsonSerializerContext
{
}

// AOT-compatible response types (replacing anonymous types)
public record SuccessResponse(string Message, string OrderId);
public record MessageResponse(string Message);
public record ShipResponse(string Message, string OrderId, string TrackingNumber);
public record ErrorResponse(string Error);
public record FlowInfoResponse(string Name, string[] Steps, string[] Tags, string Version);
public record FlowRegisteredResponse(string Name, DateTime RegisteredAt);
public record FlowReloadedResponse(string Name, int OldVersion, int NewVersion);
public record EventCreatedResponse(string OrderId, string StreamId, int EventCount);
public record ProjectionRebuildResponse(string Message, int TotalOrders);
public record SubscriptionCreatedResponse(string Name, string Pattern);
public record SubscriptionProcessedResponse(string Name, int ProcessedCount);
public record StreamVerifyResponse(string StreamId, bool IsValid, string? Hash, string? Error);
public record SnapshotCreatedResponse(string StreamId, int Version);
public record UserRegisteredResponse(string UserId, string Email, string FullName, string Token, string TokenType, int ExpiresIn);
public record CurrentUserResponse(string UserId, string? Email, string? FullName, string? Role);
