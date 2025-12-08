using Catga;
using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.EventSourcing;
using Catga.Persistence.InMemory.Stores;
using Catga.Persistence.Stores;
using Catga.Resilience;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ============================================
// 1. Catga Core Setup
// ============================================
builder.Services
    .AddCatga(opt => { if (builder.Environment.IsDevelopment()) opt.ForDevelopment(); else opt.Minimal(); })
    .UseMemoryPack();

// Transport: InMemory (default) | Redis | NATS
var transport = Environment.GetEnvironmentVariable("CATGA_TRANSPORT") ?? "InMemory";
_ = transport.ToLower() switch
{
    "redis" => builder.Services.AddRedisTransport(Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379"),
    "nats" => builder.Services.AddNatsTransport(Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222"),
    _ => builder.Services.AddInMemoryTransport()
};

// Persistence: InMemory (default) | Redis
builder.Services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
var persistence = Environment.GetEnvironmentVariable("CATGA_PERSISTENCE") ?? "InMemory";
if (persistence.Equals("redis", StringComparison.OrdinalIgnoreCase))
{
    var conn = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379";
    builder.Services.AddRedisPersistence(conn).AddRedisEventStore().AddRedisSnapshotStore();
}
else
{
    builder.Services.AddInMemoryPersistence();
    builder.Services.AddSingleton<IEventStore, InMemoryEventStore>();
}

// ============================================
// 2. Application Services (Auto-registered by source generator)
// ============================================
builder.Services.AddCatgaHandlers(); // Auto-registers all handlers, behaviors, projections

// Infrastructure services (not auto-discovered)
builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
builder.Services.AddSingleton<InMemoryProjectionCheckpointStore>();
builder.Services.AddSingleton<InMemorySubscriptionStore>();
builder.Services.AddSingleton<InMemoryAuditLogStore>();
builder.Services.AddSingleton<IAuditLogStore>(sp => sp.GetRequiredService<InMemoryAuditLogStore>());
builder.Services.AddSingleton<InMemoryGdprStore>();
builder.Services.AddSingleton<IGdprStore>(sp => sp.GetRequiredService<InMemoryGdprStore>());
builder.Services.AddSingleton<OrderAuditService>();
builder.Services.AddSingleton<EnhancedInMemorySnapshotStore>();
builder.Services.AddSingleton<IEnhancedSnapshotStore>(sp => sp.GetRequiredService<EnhancedInMemorySnapshotStore>());
builder.Services.AddOrderEventVersioning();
builder.Services.AddTimeTravelService<OrderAggregate>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHealthChecks("/health");

// ============================================
// 3. API Endpoints
// ============================================

// Orders CRUD
app.MapPost("/api/orders", async (CreateOrderCommand cmd, ICatgaMediator m) =>
{
    var r = await m.SendAsync<CreateOrderCommand, OrderCreatedResult>(cmd);
    return r.IsSuccess ? Results.Ok(r.Value) : Results.BadRequest(r.Error);
}).WithTags("Orders");

app.MapPost("/api/orders/flow", async (CreateOrderFlowCommand cmd, ICatgaMediator m) =>
{
    var r = await m.SendAsync<CreateOrderFlowCommand, OrderCreatedResult>(cmd);
    return r.IsSuccess ? Results.Ok(r.Value) : Results.BadRequest(r.Error);
}).WithTags("Orders").WithDescription("Create order with Flow pattern (Saga with compensation)");

app.MapGet("/api/orders/{orderId}", async (string orderId, ICatgaMediator m) =>
{
    var r = await m.SendAsync<GetOrderQuery, Order?>(new(orderId));
    return r.Value is null ? Results.NotFound() : Results.Ok(r.Value);
}).WithTags("Orders");

app.MapPost("/api/orders/{orderId}/cancel", async (string orderId, CancelOrderCommand? body, ICatgaMediator m) =>
{
    var r = await m.SendAsync(new CancelOrderCommand(orderId, body?.Reason));
    return r.IsSuccess ? Results.Ok() : Results.BadRequest(r.Error);
}).WithTags("Orders");

app.MapGet("/api/users/{customerId}/orders", async (string customerId, ICatgaMediator m) =>
{
    var r = await m.SendAsync<GetUserOrdersQuery, List<Order>>(new(customerId));
    return Results.Ok(r.Value);
}).WithTags("Orders");

// Time Travel (Event Sourcing)
var tt = app.MapGroup("/api/timetravel").WithTags("Time Travel");
tt.MapPost("/demo/create", async (IEventStore es) =>
{
    var orderId = $"order-{Guid.NewGuid():N}"[..16];
    var streamId = $"OrderAggregate-{orderId}";
    var msgId = Random.Shared.NextInt64();
    var events = new IEvent[]
    {
        new OrderAggregateCreated { MessageId = msgId++, OrderId = orderId, CustomerId = "customer-001", InitialAmount = 0 },
        new OrderItemAdded { MessageId = msgId++, OrderId = orderId, ProductName = "Laptop", Quantity = 1, Price = 999.99m },
        new OrderItemAdded { MessageId = msgId++, OrderId = orderId, ProductName = "Mouse", Quantity = 2, Price = 29.99m },
        new OrderStatusChanged { MessageId = msgId++, OrderId = orderId, NewStatus = "Processing" },
        new OrderDiscountApplied { MessageId = msgId++, OrderId = orderId, DiscountAmount = 50m },
        new OrderStatusChanged { MessageId = msgId++, OrderId = orderId, NewStatus = "Confirmed" }
    };
    await es.AppendAsync(streamId, events);
    return Results.Ok(new { orderId, eventCount = events.Length });
});
tt.MapGet("/orders/{orderId}/version/{version:long}", async (string orderId, long version, ITimeTravelService<OrderAggregate> tts) =>
{
    var state = await tts.GetStateAtVersionAsync(orderId, version);
    return state == null ? Results.NotFound() : Results.Ok(new { state.Id, state.CustomerId, state.TotalAmount, state.Status, state.Version });
});
tt.MapGet("/orders/{orderId}/history", async (string orderId, ITimeTravelService<OrderAggregate> tts) =>
{
    var h = await tts.GetVersionHistoryAsync(orderId);
    return Results.Ok(h.Select(x => new { x.Version, x.EventType, x.Timestamp }));
});

// Projections (Read Models)
var proj = app.MapGroup("/api/projections").WithTags("Projections");
proj.MapGet("/order-summary", (OrderSummaryProjection p) => Results.Ok(new { p.TotalOrders, p.TotalRevenue, p.OrdersByStatus }));
proj.MapPost("/order-summary/rebuild", async (OrderSummaryProjection p, InMemoryProjectionCheckpointStore cs, IEventStore es) =>
{
    var rebuilder = new ProjectionRebuilder<OrderSummaryProjection>(es, cs, p, p.Name);
    await rebuilder.RebuildAsync();
    return Results.Ok(new { message = "Rebuilt", p.TotalOrders });
});
proj.MapGet("/customer-stats", (CustomerStatsProjection p) => Results.Ok(p.Stats.Values));

// Subscriptions (Persistent event handlers)
var sub = app.MapGroup("/api/subscriptions").WithTags("Subscriptions");
sub.MapGet("/", async (InMemorySubscriptionStore s) => Results.Ok((await s.ListAsync()).Select(x => new { x.Name, x.StreamPattern, x.Position, x.ProcessedCount })));
sub.MapPost("/", async (string name, string pattern, InMemorySubscriptionStore s) =>
{
    await s.SaveAsync(new PersistentSubscription(name, pattern));
    return Results.Created($"/api/subscriptions/{name}", new { name, pattern });
});
sub.MapPost("/{name}/process", async (string name, InMemorySubscriptionStore s, IEventStore es, OrderEventSubscriptionHandler h) =>
{
    var runner = new SubscriptionRunner(es, s, h);
    await runner.RunOnceAsync(name);
    var x = await s.LoadAsync(name);
    return Results.Ok(new { name, processedCount = x?.ProcessedCount ?? 0 });
});

// Audit & Compliance
var audit = app.MapGroup("/api/audit").WithTags("Audit");
audit.MapGet("/logs/{streamId}", async (string streamId, OrderAuditService a) => Results.Ok(await a.GetLogsAsync(streamId)));
audit.MapPost("/verify/{streamId}", async (string streamId, OrderAuditService a) =>
{
    var r = await a.VerifyStreamAsync(streamId);
    return Results.Ok(new { streamId, r.IsValid, r.Hash, r.Error });
});
audit.MapPost("/gdpr/erasure-request", async (string customerId, string requestedBy, OrderAuditService a) =>
{
    await a.RequestCustomerErasureAsync(customerId, requestedBy);
    return Results.Accepted();
});
audit.MapGet("/gdpr/pending-requests", async (OrderAuditService a) => Results.Ok(await a.GetPendingErasureRequestsAsync()));

// Snapshots (Performance optimization)
var snap = app.MapGroup("/api/snapshots").WithTags("Snapshots");
snap.MapPost("/orders/{orderId}", async (string orderId, IEnhancedSnapshotStore ss, IEventStore es) =>
{
    var streamId = $"OrderAggregate-{orderId}";
    var stream = await es.ReadAsync(streamId);
    if (stream.Events.Count == 0) return Results.NotFound();
    var agg = new OrderAggregate();
    foreach (var e in stream.Events) agg.Apply(e.Event);
    await ss.SaveAsync(streamId, agg, stream.Version);
    return Results.Ok(new { streamId, version = stream.Version });
});
snap.MapGet("/orders/{orderId}/history", async (string orderId, IEnhancedSnapshotStore ss) =>
    Results.Ok(await ss.GetSnapshotHistoryAsync($"OrderAggregate-{orderId}")));

app.Run();

namespace OrderSystem.Api { public partial class Program; }
