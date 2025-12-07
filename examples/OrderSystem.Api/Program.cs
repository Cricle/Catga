using Catga;
using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.EventSourcing;
using Catga.Pipeline;
using Catga.Persistence.InMemory.Stores;
using Catga.Persistence.Stores;
using Catga.Resilience;
using OrderSystem.Api.Behaviors;
using OrderSystem.Api.Domain;
using OrderSystem.Api.EventSourcing;
using OrderSystem.Api.Handlers;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ============================================
// Catga Configuration (Best Practices)
// ============================================
// Use Minimal() for production (disables logging/tracing for max performance)
// Use ForDevelopment() for development (enables detailed logging)
var isDevelopment = builder.Environment.IsDevelopment();
builder.Services
    .AddCatga(options =>
    {
        if (isDevelopment)
            options.ForDevelopment();  // Detailed logging for debugging
        else
            options.Minimal();         // Max performance for production
    })
    .UseMemoryPack();                  // High-performance binary serialization

// Transport configuration (env: CATGA_TRANSPORT = InMemory | Redis | NATS)
var transport = Environment.GetEnvironmentVariable("CATGA_TRANSPORT") ?? "InMemory";
switch (transport.ToLower())
{
    case "redis":
        var redisConn = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379";
        builder.Services.AddRedisTransport(redisConn);
        break;
    case "nats":
        var natsUrl = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";
        builder.Services.AddNatsTransport(natsUrl);
        break;
    default:
        builder.Services.AddInMemoryTransport();
        break;
}

// Resilience pipeline (required for persistence)
builder.Services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();

// Persistence configuration (env: CATGA_PERSISTENCE = InMemory | Redis)
var persistence = Environment.GetEnvironmentVariable("CATGA_PERSISTENCE") ?? "InMemory";
switch (persistence.ToLower())
{
    case "redis":
        var redisConnPersist = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379";
        builder.Services.AddRedisPersistence(redisConnPersist);
        builder.Services.AddRedisEventStore();
        builder.Services.AddRedisSnapshotStore();
        break;
    default:
        builder.Services.AddInMemoryPersistence();
        builder.Services.AddSingleton<IEventStore, InMemoryEventStore>();
        break;
}

// ============================================
// Services
// ============================================
builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();

// ============================================
// Pipeline Behaviors (Cross-cutting concerns)
// ============================================
builder.Services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
builder.Services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

// ============================================
// Command/Query Handlers
// Note: Use Singleton for stateless handlers (ICatgaMediator is now Singleton)
// ============================================
builder.Services.AddSingleton<IRequestHandler<CreateOrderCommand, OrderCreatedResult>, CreateOrderHandler>();
builder.Services.AddSingleton<IRequestHandler<CreateOrderFlowCommand, OrderCreatedResult>, CreateOrderFlowHandler>();
builder.Services.AddSingleton<IRequestHandler<CancelOrderCommand>, CancelOrderHandler>();
builder.Services.AddSingleton<IRequestHandler<GetOrderQuery, Order?>, GetOrderHandler>();
builder.Services.AddSingleton<IRequestHandler<GetUserOrdersQuery, List<Order>>, GetUserOrdersHandler>();

// ============================================
// Event Handlers (Multiple handlers per event)
// Note: Use Singleton for stateless handlers (ICatgaMediator is now Singleton)
// ============================================
builder.Services.AddSingleton<IEventHandler<OrderCreatedEvent>, OrderCreatedEventHandler>();
builder.Services.AddSingleton<IEventHandler<OrderCreatedEvent>, SendOrderNotificationHandler>();
builder.Services.AddSingleton<IEventHandler<OrderCancelledEvent>, OrderCancelledEventHandler>();
builder.Services.AddSingleton<IEventHandler<OrderConfirmedEvent>, OrderConfirmedEventHandler>();

// ============================================
// Time Travel Service
// ============================================
builder.Services.AddTimeTravelService<OrderAggregate>();

// ============================================
// Event Sourcing Features (NEW)
// ============================================

// Projections
builder.Services.AddSingleton<OrderSummaryProjection>();
builder.Services.AddSingleton<CustomerStatsProjection>();
builder.Services.AddSingleton<InMemoryProjectionCheckpointStore>();

// Subscriptions
builder.Services.AddSingleton<InMemorySubscriptionStore>();
builder.Services.AddSingleton<OrderEventSubscriptionHandler>();
builder.Services.AddSingleton<OrderNotificationHandler>();

// Event Versioning
builder.Services.AddOrderEventVersioning();

// Audit & Compliance
builder.Services.AddSingleton<InMemoryAuditLogStore>();
builder.Services.AddSingleton<IAuditLogStore>(sp => sp.GetRequiredService<InMemoryAuditLogStore>());
builder.Services.AddSingleton<InMemoryGdprStore>();
builder.Services.AddSingleton<IGdprStore>(sp => sp.GetRequiredService<InMemoryGdprStore>());
builder.Services.AddSingleton<OrderAuditService>();

// Enhanced Snapshots
builder.Services.AddSingleton<EnhancedInMemorySnapshotStore>();
builder.Services.AddSingleton<IEnhancedSnapshotStore>(sp => sp.GetRequiredService<EnhancedInMemorySnapshotStore>());

// ============================================
// Swagger & Health Checks
// ============================================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthChecks("/health");

// Endpoints
app.MapPost("/api/orders", async (CreateOrderCommand cmd, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(cmd);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
});

// Flow endpoint - demonstrates automatic compensation on failure
app.MapPost("/api/orders/flow", async (CreateOrderFlowCommand cmd, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<CreateOrderFlowCommand, OrderCreatedResult>(cmd);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
}).WithDescription("Create order using Flow pattern with automatic compensation");

app.MapGet("/api/orders/{orderId}", async (string orderId, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<GetOrderQuery, Order?>(new(orderId));
    return result.Value is null ? Results.NotFound() : Results.Ok(result.Value);
});

app.MapPost("/api/orders/{orderId}/cancel", async (string orderId, CancelOrderCommand? body, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync(new CancelOrderCommand(orderId, body?.Reason));
    return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
});

app.MapGet("/api/users/{customerId}/orders", async (string customerId, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<GetUserOrdersQuery, List<Order>>(new(customerId));
    return Results.Ok(result.Value);
});

// ============================================
// Time Travel Demo Endpoints
// ============================================
var timeTravelGroup = app.MapGroup("/api/timetravel").WithTags("Time Travel");

// Create a demo order with events
timeTravelGroup.MapPost("/demo/create", async (IEventStore eventStore) =>
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
        new OrderItemAdded { MessageId = msgId++, OrderId = orderId, ProductName = "Keyboard", Quantity = 1, Price = 79.99m },
        new OrderStatusChanged { MessageId = msgId++, OrderId = orderId, NewStatus = "Confirmed" }
    };

    await eventStore.AppendAsync(streamId, events);

    return Results.Ok(new { orderId, eventCount = events.Length, message = "Demo order created with 7 events" });
}).WithDescription("Create a demo order with multiple events for time travel demonstration");

// Get state at specific version
timeTravelGroup.MapGet("/orders/{orderId}/version/{version:long}", async (
    string orderId,
    long version,
    ITimeTravelService<OrderAggregate> timeTravelService) =>
{
    var state = await timeTravelService.GetStateAtVersionAsync(orderId, version);
    if (state == null) return Results.NotFound(new { error = "Order not found or version does not exist" });

    return Results.Ok(new
    {
        orderId = state.Id,
        customerId = state.CustomerId,
        items = state.Items,
        totalAmount = state.TotalAmount,
        status = state.Status,
        version = state.Version,
        createdAt = state.CreatedAt,
        updatedAt = state.UpdatedAt
    });
}).WithDescription("Get order state at a specific version");

// Get version history
timeTravelGroup.MapGet("/orders/{orderId}/history", async (
    string orderId,
    ITimeTravelService<OrderAggregate> timeTravelService) =>
{
    var history = await timeTravelService.GetVersionHistoryAsync(orderId);
    return Results.Ok(history.Select(h => new
    {
        version = h.Version,
        eventType = h.EventType,
        timestamp = h.Timestamp
    }));
}).WithDescription("Get version history for an order");

// Compare two versions
timeTravelGroup.MapGet("/orders/{orderId}/compare/{fromVersion:long}/{toVersion:long}", async (
    string orderId,
    long fromVersion,
    long toVersion,
    ITimeTravelService<OrderAggregate> timeTravelService) =>
{
    var comparison = await timeTravelService.CompareVersionsAsync(orderId, fromVersion, toVersion);

    return Results.Ok(new
    {
        fromVersion,
        toVersion,
        fromState = comparison.FromState == null ? null : new
        {
            totalAmount = comparison.FromState.TotalAmount,
            status = comparison.FromState.Status,
            itemCount = comparison.FromState.Items.Count
        },
        toState = comparison.ToState == null ? null : new
        {
            totalAmount = comparison.ToState.TotalAmount,
            status = comparison.ToState.Status,
            itemCount = comparison.ToState.Items.Count
        },
        eventsBetween = comparison.EventsBetween.Select(e => new
        {
            version = e.Version,
            eventType = e.EventType,
            timestamp = e.Timestamp
        })
    });
}).WithDescription("Compare order state between two versions");

// Get all versions with full state
timeTravelGroup.MapGet("/orders/{orderId}/timeline", async (
    string orderId,
    ITimeTravelService<OrderAggregate> timeTravelService) =>
{
    var history = await timeTravelService.GetVersionHistoryAsync(orderId);
    var timeline = new List<object>();

    foreach (var h in history)
    {
        var state = await timeTravelService.GetStateAtVersionAsync(orderId, h.Version);
        if (state != null)
        {
            timeline.Add(new
            {
                version = h.Version,
                eventType = h.EventType,
                timestamp = h.Timestamp,
                state = new
                {
                    totalAmount = state.TotalAmount,
                    status = state.Status,
                    itemCount = state.Items.Count,
                    items = state.Items.Select(i => new { i.ProductName, i.Quantity, i.Price })
                }
            });
        }
    }

    return Results.Ok(timeline);
}).WithDescription("Get complete timeline with state at each version");

// ============================================
// Projection Endpoints (NEW)
// ============================================
var projectionGroup = app.MapGroup("/api/projections").WithTags("Projections");

projectionGroup.MapGet("/order-summary", (OrderSummaryProjection projection) =>
{
    return Results.Ok(new
    {
        totalOrders = projection.TotalOrders,
        totalRevenue = projection.TotalRevenue,
        ordersByStatus = projection.OrdersByStatus,
        orders = projection.Orders.Values.Take(10)
    });
}).WithDescription("Get order summary projection data");

projectionGroup.MapPost("/order-summary/rebuild", async (
    OrderSummaryProjection projection,
    InMemoryProjectionCheckpointStore checkpointStore,
    IEventStore eventStore) =>
{
    var rebuilder = new ProjectionRebuilder<OrderSummaryProjection>(eventStore, checkpointStore, projection, projection.Name);
    await rebuilder.RebuildAsync();
    return Results.Ok(new { message = "Projection rebuilt", totalOrders = projection.TotalOrders });
}).WithDescription("Rebuild order summary projection from events");

projectionGroup.MapGet("/customer-stats", (CustomerStatsProjection projection) =>
{
    return Results.Ok(projection.Stats.Values);
}).WithDescription("Get customer statistics projection");

// ============================================
// Subscription Endpoints (NEW)
// ============================================
var subscriptionGroup = app.MapGroup("/api/subscriptions").WithTags("Subscriptions");

subscriptionGroup.MapGet("/", async (InMemorySubscriptionStore store) =>
{
    var subs = await store.ListAsync();
    return Results.Ok(subs.Select(s => new
    {
        s.Name,
        s.StreamPattern,
        s.Position,
        s.ProcessedCount,
        s.LastProcessedAt
    }));
}).WithDescription("List all subscriptions");

subscriptionGroup.MapPost("/", async (
    string name,
    string pattern,
    InMemorySubscriptionStore store) =>
{
    var sub = new PersistentSubscription(name, pattern);
    await store.SaveAsync(sub);
    return Results.Created($"/api/subscriptions/{name}", new { name, pattern });
}).WithDescription("Create a new subscription");

subscriptionGroup.MapPost("/{name}/process", async (
    string name,
    InMemorySubscriptionStore store,
    IEventStore eventStore,
    OrderEventSubscriptionHandler handler) =>
{
    var runner = new SubscriptionRunner(eventStore, store, handler);
    await runner.RunOnceAsync(name);
    var sub = await store.LoadAsync(name);
    return Results.Ok(new { name, processedCount = sub?.ProcessedCount ?? 0 });
}).WithDescription("Process events for a subscription");

// ============================================
// Audit & Compliance Endpoints (NEW)
// ============================================
var auditGroup = app.MapGroup("/api/audit").WithTags("Audit & Compliance");

auditGroup.MapGet("/logs/{streamId}", async (string streamId, OrderAuditService auditService) =>
{
    var logs = await auditService.GetLogsAsync(streamId);
    return Results.Ok(logs);
}).WithDescription("Get audit logs for a stream");

auditGroup.MapPost("/verify/{streamId}", async (string streamId, OrderAuditService auditService) =>
{
    var result = await auditService.VerifyStreamAsync(streamId);
    return Results.Ok(new { streamId, result.IsValid, result.Hash, result.Error });
}).WithDescription("Verify stream integrity");

auditGroup.MapPost("/gdpr/erasure-request", async (
    string customerId,
    string requestedBy,
    OrderAuditService auditService) =>
{
    await auditService.RequestCustomerErasureAsync(customerId, requestedBy);
    return Results.Accepted();
}).WithDescription("Request GDPR data erasure for a customer");

auditGroup.MapGet("/gdpr/pending-requests", async (OrderAuditService auditService) =>
{
    var requests = await auditService.GetPendingErasureRequestsAsync();
    return Results.Ok(requests);
}).WithDescription("Get pending GDPR erasure requests");

// ============================================
// Snapshot Endpoints (NEW)
// ============================================
var snapshotGroup = app.MapGroup("/api/snapshots").WithTags("Snapshots");

snapshotGroup.MapPost("/orders/{orderId}", async (
    string orderId,
    IEnhancedSnapshotStore snapshotStore,
    ITimeTravelService<OrderAggregate> timeTravelService) =>
{
    var streamId = $"OrderAggregate-{orderId}";
    var state = await timeTravelService.GetStateAtVersionAsync(orderId, long.MaxValue);
    if (state == null) return Results.NotFound();

    await snapshotStore.SaveAsync(streamId, state, state.Version);
    return Results.Ok(new { streamId, version = state.Version, message = "Snapshot created" });
}).WithDescription("Create a snapshot for an order");

snapshotGroup.MapGet("/orders/{orderId}/history", async (
    string orderId,
    IEnhancedSnapshotStore snapshotStore) =>
{
    var streamId = $"OrderAggregate-{orderId}";
    var history = await snapshotStore.GetSnapshotHistoryAsync(streamId);
    return Results.Ok(history);
}).WithDescription("Get snapshot history for an order");

snapshotGroup.MapGet("/orders/{orderId}/version/{version:long}", async (
    string orderId,
    long version,
    IEnhancedSnapshotStore snapshotStore) =>
{
    var streamId = $"OrderAggregate-{orderId}";
    var snapshot = await snapshotStore.LoadAtVersionAsync<OrderAggregate>(streamId, version);
    if (!snapshot.HasValue) return Results.NotFound();

    return Results.Ok(new
    {
        version = snapshot.Value.Version,
        state = new
        {
            snapshot.Value.State.Id,
            snapshot.Value.State.CustomerId,
            snapshot.Value.State.TotalAmount,
            snapshot.Value.State.Status
        }
    });
}).WithDescription("Load snapshot at specific version");

app.Run();

namespace OrderSystem.Api
{
    public partial class Program;
}
