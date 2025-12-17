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
[JsonSerializable(typeof(FlowListResponse))]
[JsonSerializable(typeof(FlowDetailsResponse))]
[JsonSerializable(typeof(FlowRegisteredResponse2))]
[JsonSerializable(typeof(FlowReloadedResponse2))]
[JsonSerializable(typeof(FlowVersionResponse))]
[JsonSerializable(typeof(ReloadEventInfoResponse))]
[JsonSerializable(typeof(TimeTravelDemoResponse))]
[JsonSerializable(typeof(ProjectionRebuildResponse2))]
[JsonSerializable(typeof(SubscriptionCreatedResponse2))]
[JsonSerializable(typeof(SubscriptionProcessedResponse2))]
[JsonSerializable(typeof(StreamVerifyResponse2))]
[JsonSerializable(typeof(SnapshotCreatedResponse2))]
[JsonSerializable(typeof(MetricsResponse))]
[JsonSerializable(typeof(DemoRecordFlowResponse))]
[JsonSerializable(typeof(DemoRecordFailureResponse))]
[JsonSerializable(typeof(SyncStatusResponse))]
[JsonSerializable(typeof(RebuildReadModelResponse))]
// EventSourcing types
[JsonSerializable(typeof(Catga.EventSourcing.PersistentSubscription))]
[JsonSerializable(typeof(IReadOnlyList<Catga.EventSourcing.PersistentSubscription>))]
[JsonSerializable(typeof(OrderSystem.Api.Domain.OrderSummaryProjection))]
[JsonSerializable(typeof(OrderSystem.Api.Domain.CustomerStatsProjection))]
[JsonSerializable(typeof(OrderSystem.Api.Domain.OrderAggregate))]
[JsonSerializable(typeof(Catga.EventSourcing.ChangeRecord))]
[JsonSerializable(typeof(IReadOnlyList<Catga.EventSourcing.ChangeRecord>))]
[JsonSerializable(typeof(List<Catga.EventSourcing.ChangeRecord>))]
[JsonSerializable(typeof(Catga.EventSourcing.VersionInfo))]
[JsonSerializable(typeof(IReadOnlyList<Catga.EventSourcing.VersionInfo>))]
// Health check types
[JsonSerializable(typeof(OrderSystem.Api.Infrastructure.HealthCheckResponse))]
[JsonSerializable(typeof(OrderSystem.Api.Infrastructure.HealthCheckEntry))]
[JsonSerializable(typeof(List<OrderSystem.Api.Infrastructure.HealthCheckEntry>))]
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
// HotReload endpoints
public record FlowListResponse(int Count, List<string> Flows);
public record FlowDetailsResponse(string FlowName, int Version, string ConfigType, bool Registered);
public record FlowRegisteredResponse2(string Message, int Version);
public record FlowReloadedResponse2(string Message, int OldVersion, int NewVersion);
public record FlowVersionResponse(string FlowName, int CurrentVersion);
public record ReloadEventInfoResponse(string EventType, string[] Properties, string Usage);
// EventSourcing endpoints
public record TimeTravelDemoResponse(string OrderId, string StreamId, int EventCount);
public record ProjectionRebuildResponse2(string Message, long TotalOrders);
public record SubscriptionCreatedResponse2(string Name, string Pattern);
public record SubscriptionProcessedResponse2(string Name, long ProcessedCount);
public record StreamVerifyResponse2(string StreamId, bool IsValid, string? Hash, string? Error);
public record SnapshotCreatedResponse2(string StreamId, long Version);
// Observability endpoints
public record MetricsResponse(Dictionary<string, object> Metrics);
public record DemoRecordFlowResponse(string FlowName, string FlowId, string Message);
public record DemoRecordFailureResponse(string FlowName, string FlowId, string Error);
// ReadModelSync endpoints
public record SyncStatusResponse(string Status, int SyncedCount, DateTime LastSyncTime);
public record RebuildReadModelResponse(string Message, int ProcessedCount);
