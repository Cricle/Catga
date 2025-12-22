// Catga OrderSystem Example - Complete CQRS Demo
// Features: InMemory/Redis/NATS, Standalone/Cluster, Event Sourcing
// Usage: dotnet run -- [--transport inmemory|redis|nats] [--persistence inmemory|redis|nats] [--cluster] [--port 5000]

using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Catga;
using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using MemoryPack;
using OrderSystem;

var builder = WebApplication.CreateSlimBuilder(args);

// Parse command line arguments
var transport = GetArg(args, "--transport") ?? "inmemory";
var persistence = GetArg(args, "--persistence") ?? "inmemory";
var redisConn = GetArg(args, "--redis") ?? "localhost:6379";
var natsUrl = GetArg(args, "--nats") ?? "nats://localhost:4222";
var isCluster = args.Contains("--cluster");
var nodeId = GetArg(args, "--node-id") ?? $"node-{Guid.NewGuid().ToString("N")[..6]}";
var port = int.Parse(GetArg(args, "--port") ?? "5000");

builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Configure Catga with MemoryPack serialization
var catga = builder.Services.AddCatga().UseMemoryPack();

// Configure persistence backend
switch (persistence.ToLower())
{
    case "redis": 
        catga.UseRedis(redisConn); 
        Console.WriteLine($"✓ Persistence: Redis ({redisConn})");
        break;
    case "nats": 
        builder.Services.AddNatsConnection(natsUrl); 
        catga.UseNats(); 
        Console.WriteLine($"✓ Persistence: NATS ({natsUrl})");
        break;
    default: 
        catga.UseInMemory(); 
        Console.WriteLine("✓ Persistence: InMemory");
        break;
}

// Configure transport backend
switch (transport.ToLower())
{
    case "redis": 
        builder.Services.AddRedisTransport(redisConn); 
        Console.WriteLine($"✓ Transport: Redis ({redisConn})");
        break;
    case "nats": 
        builder.Services.AddNatsTransport(natsUrl); 
        Console.WriteLine($"✓ Transport: NATS ({natsUrl})");
        break;
    default: 
        builder.Services.AddInMemoryTransport(); 
        Console.WriteLine("✓ Transport: InMemory");
        break;
}

// Register handlers and services
builder.Services.AddCatgaHandlers();
builder.Services.AddSingleton<OrderStore>();
builder.Services.AddSingleton(new NodeInfo(nodeId, isCluster, transport, persistence));
builder.Services.ConfigureHttpJsonOptions(opt =>
    opt.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));

var app = builder.Build();

// Health and system info endpoints
app.MapGet("/", (NodeInfo node) => Results.Ok(new SystemInfoResponse(
    Service: "Catga OrderSystem",
    Version: "1.0.0",
    Node: node.NodeId,
    Mode: node.IsCluster ? "Cluster" : "Standalone",
    Transport: node.Transport,
    Persistence: node.Persistence,
    Status: "running",
    Timestamp: DateTime.UtcNow
)));

app.MapGet("/health", () => Results.Ok(new HealthResponse("healthy", DateTime.UtcNow)));

app.MapPost("/orders", async (CreateOrderRequest req, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(new CreateOrderCommand(req.CustomerId, req.Items));
    return result.IsSuccess ? Results.Created($"/orders/{result.Value!.OrderId}", result.Value) : Results.BadRequest(result.Error);
});

app.MapGet("/orders/{id}", async (string id, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<GetOrderQuery, Order?>(new GetOrderQuery(id));
    return result.IsSuccess && result.Value != null ? Results.Ok(result.Value) : Results.NotFound();
});

app.MapGet("/orders", async (ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<GetAllOrdersQuery, List<Order>>(new GetAllOrdersQuery());
    return Results.Ok(result.Value ?? new List<Order>());
});

app.MapPost("/orders/{id}/pay", async (string id, PayOrderRequest req, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<PayOrderCommand>(new PayOrderCommand(id, req.PaymentMethod));
    return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
});

app.MapPost("/orders/{id}/ship", async (string id, ShipOrderRequest req, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<ShipOrderCommand>(new ShipOrderCommand(id, req.TrackingNumber));
    return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
});

app.MapPost("/orders/{id}/cancel", async (string id, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<CancelOrderCommand>(new CancelOrderCommand(id));
    return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
});

app.MapGet("/orders/{id}/history", (string id, OrderStore store) => 
{
    var events = store.GetEvents(id);
    return events.Count > 0 ? Results.Ok(events) : Results.NotFound();
});

// Statistics endpoint
app.MapGet("/stats", (OrderStore store) =>
{
    var orders = store.GetAll();
    var byStatus = orders.GroupBy(o => o.Status).ToDictionary(g => g.Key.ToString(), g => g.Count());
    var totalRevenue = orders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.Total);
    
    return Results.Ok(new StatsResponse(
        TotalOrders: orders.Count,
        ByStatus: byStatus,
        TotalRevenue: totalRevenue,
        Timestamp: DateTime.UtcNow
    ));
});

Console.WriteLine($@"
╔══════════════════════════════════════════════════════════════╗
║              Catga OrderSystem - Running                     ║
╠══════════════════════════════════════════════════════════════╣
║ Mode:        {(isCluster ? $"Cluster ({nodeId})" : "Standalone"),-45} ║
║ Port:        {port,-45} ║
║ Transport:   {transport,-45} ║
║ Persistence: {persistence,-45} ║
╠══════════════════════════════════════════════════════════════╣
║ Endpoints:                                                   ║
║   GET  /                    - System info                    ║
║   GET  /health              - Health check                   ║
║   GET  /stats               - Statistics                     ║
║   POST /orders              - Create order                   ║
║   GET  /orders              - List orders                    ║
║   GET  /orders/{{id}}         - Get order                      ║
║   POST /orders/{{id}}/pay     - Pay order                      ║
║   POST /orders/{{id}}/ship    - Ship order                     ║
║   POST /orders/{{id}}/cancel  - Cancel order                   ║
║   GET  /orders/{{id}}/history - Event history                  ║
╚══════════════════════════════════════════════════════════════╝
");

app.Run();

static string? GetArg(string[] args, string name)
{
    var idx = Array.IndexOf(args, name);
    return idx >= 0 && idx < args.Length - 1 ? args[idx + 1] : null;
}

namespace OrderSystem
{
    public enum OrderStatus { Pending, Paid, Shipped, Delivered, Cancelled }

    [MemoryPackable] public partial record OrderItem(string ProductId, string Name, int Quantity, decimal Price);
    [MemoryPackable] public partial record Order(string Id, string CustomerId, List<OrderItem> Items, OrderStatus Status, decimal Total, DateTime CreatedAt, DateTime? PaidAt = null, DateTime? ShippedAt = null, string? TrackingNumber = null);
    [MemoryPackable] public partial record CreateOrderCommand(string CustomerId, List<OrderItem> Items) : IRequest<OrderCreatedResult> { public long MessageId { get; init; } }
    [MemoryPackable] public partial record PayOrderCommand(string OrderId, string PaymentMethod) : IRequest { public long MessageId { get; init; } }
    [MemoryPackable] public partial record ShipOrderCommand(string OrderId, string TrackingNumber) : IRequest { public long MessageId { get; init; } }
    [MemoryPackable] public partial record CancelOrderCommand(string OrderId) : IRequest { public long MessageId { get; init; } }
    [MemoryPackable] public partial record GetOrderQuery(string OrderId) : IRequest<Order?> { public long MessageId { get; init; } }
    [MemoryPackable] public partial record GetAllOrdersQuery : IRequest<List<Order>> { public long MessageId { get; init; } }
    [MemoryPackable] public partial record OrderCreatedResult(string OrderId, decimal Total, DateTime CreatedAt);
    [MemoryPackable] public partial record OrderCreatedEvent(string OrderId, string CustomerId, decimal Total, DateTime CreatedAt) : IEvent { public long MessageId { get; init; } }
    [MemoryPackable] public partial record OrderPaidEvent(string OrderId, string PaymentMethod, DateTime PaidAt) : IEvent { public long MessageId { get; init; } }
    [MemoryPackable] public partial record OrderShippedEvent(string OrderId, string TrackingNumber, DateTime ShippedAt) : IEvent { public long MessageId { get; init; } }
    [MemoryPackable] public partial record OrderCancelledEvent(string OrderId, DateTime CancelledAt) : IEvent { public long MessageId { get; init; } }

    public record CreateOrderRequest(string CustomerId, List<OrderItem> Items);
    public record PayOrderRequest(string PaymentMethod);
    public record ShipOrderRequest(string TrackingNumber);
    public record HealthResponse(string Status, DateTime Timestamp);
    public record SystemInfoResponse(string Service, string Version, string Node, string Mode, string Transport, string Persistence, string Status, DateTime Timestamp);
    public record StatsResponse(int TotalOrders, Dictionary<string, int> ByStatus, decimal TotalRevenue, DateTime Timestamp);

    public sealed class CreateOrderHandler(OrderStore store, ICatgaMediator mediator) : IRequestHandler<CreateOrderCommand, OrderCreatedResult>
    {
        public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(CreateOrderCommand cmd, CancellationToken ct = default)
        {
            var orderId = Guid.NewGuid().ToString("N")[..8];
            var total = cmd.Items.Sum(i => i.Price * i.Quantity);
            var now = DateTime.UtcNow;
            store.Save(new Order(orderId, cmd.CustomerId, cmd.Items, OrderStatus.Pending, total, now));
            store.AppendEvent(orderId, new OrderCreatedEvent(orderId, cmd.CustomerId, total, now));
            await mediator.PublishAsync(new OrderCreatedEvent(orderId, cmd.CustomerId, total, now), ct);
            return CatgaResult<OrderCreatedResult>.Success(new OrderCreatedResult(orderId, total, now));
        }
    }

    public sealed class PayOrderHandler(OrderStore store, ICatgaMediator mediator) : IRequestHandler<PayOrderCommand>
    {
        public async ValueTask<CatgaResult> HandleAsync(PayOrderCommand cmd, CancellationToken ct = default)
        {
            var order = store.Get(cmd.OrderId);
            if (order == null) return CatgaResult.Failure("Order not found");
            if (order.Status != OrderStatus.Pending) return CatgaResult.Failure("Order cannot be paid");
            var now = DateTime.UtcNow;
            store.Save(order with { Status = OrderStatus.Paid, PaidAt = now });
            store.AppendEvent(cmd.OrderId, new OrderPaidEvent(cmd.OrderId, cmd.PaymentMethod, now));
            await mediator.PublishAsync(new OrderPaidEvent(cmd.OrderId, cmd.PaymentMethod, now), ct);
            return CatgaResult.Success();
        }
    }

    public sealed class ShipOrderHandler(OrderStore store, ICatgaMediator mediator) : IRequestHandler<ShipOrderCommand>
    {
        public async ValueTask<CatgaResult> HandleAsync(ShipOrderCommand cmd, CancellationToken ct = default)
        {
            var order = store.Get(cmd.OrderId);
            if (order == null) return CatgaResult.Failure("Order not found");
            if (order.Status != OrderStatus.Paid) return CatgaResult.Failure("Order must be paid first");
            var now = DateTime.UtcNow;
            store.Save(order with { Status = OrderStatus.Shipped, ShippedAt = now, TrackingNumber = cmd.TrackingNumber });
            store.AppendEvent(cmd.OrderId, new OrderShippedEvent(cmd.OrderId, cmd.TrackingNumber, now));
            await mediator.PublishAsync(new OrderShippedEvent(cmd.OrderId, cmd.TrackingNumber, now), ct);
            return CatgaResult.Success();
        }
    }

    public sealed class CancelOrderHandler(OrderStore store, ICatgaMediator mediator) : IRequestHandler<CancelOrderCommand>
    {
        public async ValueTask<CatgaResult> HandleAsync(CancelOrderCommand cmd, CancellationToken ct = default)
        {
            var order = store.Get(cmd.OrderId);
            if (order == null) return CatgaResult.Failure("Order not found");
            if (order.Status is OrderStatus.Shipped or OrderStatus.Delivered) return CatgaResult.Failure("Cannot cancel shipped order");
            var now = DateTime.UtcNow;
            store.Save(order with { Status = OrderStatus.Cancelled });
            store.AppendEvent(cmd.OrderId, new OrderCancelledEvent(cmd.OrderId, now));
            await mediator.PublishAsync(new OrderCancelledEvent(cmd.OrderId, now), ct);
            return CatgaResult.Success();
        }
    }

    public sealed class GetOrderHandler(OrderStore store) : IRequestHandler<GetOrderQuery, Order?>
    {
        public ValueTask<CatgaResult<Order?>> HandleAsync(GetOrderQuery query, CancellationToken ct = default)
            => ValueTask.FromResult(CatgaResult<Order?>.Success(store.Get(query.OrderId)));
    }

    public sealed class GetAllOrdersHandler(OrderStore store) : IRequestHandler<GetAllOrdersQuery, List<Order>>
    {
        public ValueTask<CatgaResult<List<Order>>> HandleAsync(GetAllOrdersQuery query, CancellationToken ct = default)
            => ValueTask.FromResult(CatgaResult<List<Order>>.Success(store.GetAll()));
    }

    public sealed class OrderEventLogger : IEventHandler<OrderCreatedEvent>, IEventHandler<OrderPaidEvent>
    {
        public ValueTask HandleAsync(OrderCreatedEvent evt, CancellationToken ct = default) { Console.WriteLine($"[Event] Order {evt.OrderId} created: ${evt.Total}"); return ValueTask.CompletedTask; }
        public ValueTask HandleAsync(OrderPaidEvent evt, CancellationToken ct = default) { Console.WriteLine($"[Event] Order {evt.OrderId} paid via {evt.PaymentMethod}"); return ValueTask.CompletedTask; }
    }

    public sealed class OrderStore
    {
        private readonly ConcurrentDictionary<string, Order> _orders = new();
        private readonly ConcurrentDictionary<string, List<object>> _events = new();
        public void Save(Order order) => _orders[order.Id] = order;
        public Order? Get(string id) => _orders.GetValueOrDefault(id);
        public List<Order> GetAll() => [.. _orders.Values];
        public void AppendEvent(string orderId, object evt) { var events = _events.GetOrAdd(orderId, _ => []); lock (events) events.Add(evt); }
        public List<object> GetEvents(string orderId) => _events.TryGetValue(orderId, out var events) ? [.. events] : [];
    }

    public record NodeInfo(string NodeId, bool IsCluster, string Transport, string Persistence);

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
    internal partial class AppJsonContext : JsonSerializerContext;
}
