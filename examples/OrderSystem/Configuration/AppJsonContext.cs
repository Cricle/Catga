using System.Text.Json.Serialization;
using Catga.Core;
using OrderSystem.Commands;
using OrderSystem.Dtos;
using OrderSystem.Events;
using OrderSystem.Models;

namespace OrderSystem.Configuration;

[JsonSerializable(typeof(Order))]
[JsonSerializable(typeof(List<Order>))]
[JsonSerializable(typeof(OrderItem))]
[JsonSerializable(typeof(List<OrderItem>))]
[JsonSerializable(typeof(OrderCreatedResult))]
[JsonSerializable(typeof(OrderCreatedEvent))]
[JsonSerializable(typeof(OrderPaidEvent))]
[JsonSerializable(typeof(OrderShippedEvent))]
[JsonSerializable(typeof(OrderCancelledEvent))]
[JsonSerializable(typeof(CreateOrderRequest))]
[JsonSerializable(typeof(PayOrderRequest))]
[JsonSerializable(typeof(ShipOrderRequest))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(SystemInfoResponse))]
[JsonSerializable(typeof(StatsResponse))]
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSerializable(typeof(List<object>))]
[JsonSerializable(typeof(ErrorInfo))]
[JsonSerializable(typeof(OrderStatus))]
internal partial class AppJsonContext : JsonSerializerContext;
