using Catga.Dashboard.Services;
using Catga.EventSourcing;
using Catga.Persistence.Stores;
using Catga.Persistence.InMemory.Stores;
using Catga.Resilience;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

// Register event sourcing services
builder.Services.AddSingleton<IResiliencePipelineProvider, NoOpResiliencePipelineProvider>();
builder.Services.AddSingleton<IEventStore, InMemoryEventStore>();
builder.Services.AddSingleton<InMemoryProjectionCheckpointStore>();
builder.Services.AddSingleton<InMemorySubscriptionStore>();
builder.Services.AddSingleton<DashboardMetricsService>();

var app = builder.Build();

// Seed demo data
await SeedDemoData(app.Services);

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseStaticFiles();

// Dashboard API endpoints
app.MapGet("/", () => Results.File("index.html", "text/html"));
app.MapGet("/dashboard", () => Results.File("index.html", "text/html"));

// Events API
app.MapGet("/api/streams", async (IEventStore eventStore) =>
{
    var streamIds = await eventStore.GetAllStreamIdsAsync();
    var streams = new List<object>();

    foreach (var id in streamIds)
    {
        var version = await eventStore.GetVersionAsync(id);
        streams.Add(new { StreamId = id, Version = version, EventCount = version + 1 });
    }

    return Results.Ok(streams);
});

app.MapGet("/api/streams/{streamId}/events", async (string streamId, IEventStore eventStore, int? from, int? count) =>
{
    var stream = await eventStore.ReadAsync(streamId, from ?? 0, count ?? 100);
    var events = stream.Events.Select(e => new
    {
        e.Version,
        e.EventType,
        e.Timestamp,
        Data = e.Event.ToString()
    });
    return Results.Ok(new { stream.StreamId, stream.Version, Events = events });
});

app.MapGet("/api/streams/{streamId}/history", async (string streamId, IEventStore eventStore) =>
{
    var history = await eventStore.GetVersionHistoryAsync(streamId);
    return Results.Ok(history);
});

// Projections API
app.MapGet("/api/projections", (InMemoryProjectionCheckpointStore store) =>
{
    // Return demo projections
    return Results.Ok(new[]
    {
        new { Name = "OrderSummary", Position = 1234L, Status = "Running", LastUpdated = DateTime.UtcNow.AddMinutes(-1) },
        new { Name = "CustomerStats", Position = 567L, Status = "Running", LastUpdated = DateTime.UtcNow.AddMinutes(-2) },
        new { Name = "InventoryView", Position = 890L, Status = "CatchingUp", LastUpdated = DateTime.UtcNow.AddMinutes(-5) }
    });
});

app.MapPost("/api/projections/{name}/rebuild", (string name) =>
{
    return Results.Ok(new { Message = $"Projection {name} rebuild started", Status = "InProgress" });
});

// Subscriptions API
app.MapGet("/api/subscriptions", async (InMemorySubscriptionStore store) =>
{
    var subscriptions = await store.ListAsync();
    return Results.Ok(subscriptions.Select(s => new
    {
        s.Name,
        s.StreamPattern,
        s.Position,
        s.ProcessedCount,
        s.LastProcessedAt,
        Status = s.Position >= 0 ? "Active" : "New"
    }));
});

// Metrics API
app.MapGet("/api/metrics", (DashboardMetricsService metrics) =>
{
    return Results.Ok(metrics.GetMetrics());
});

app.MapGet("/api/metrics/events-per-minute", (DashboardMetricsService metrics) =>
{
    return Results.Ok(metrics.GetEventsPerMinute());
});

app.Run();

async Task SeedDemoData(IServiceProvider services)
{
    var eventStore = services.GetRequiredService<IEventStore>();
    var subscriptionStore = services.GetRequiredService<InMemorySubscriptionStore>();

    // Seed some demo events
    await eventStore.AppendAsync("Order-order-1", new Catga.Abstractions.IEvent[]
    {
        new DemoOrderCreated { OrderId = "order-1", CustomerId = "cust-1", Amount = 99.99m },
        new DemoOrderShipped { OrderId = "order-1", TrackingNumber = "TRK123" }
    });

    await eventStore.AppendAsync("Order-order-2", new Catga.Abstractions.IEvent[]
    {
        new DemoOrderCreated { OrderId = "order-2", CustomerId = "cust-2", Amount = 149.99m }
    });

    await eventStore.AppendAsync("Customer-cust-1", new Catga.Abstractions.IEvent[]
    {
        new DemoCustomerRegistered { CustomerId = "cust-1", Email = "john@example.com" }
    });

    // Seed subscriptions
    await subscriptionStore.SaveAsync(new PersistentSubscription("order-processor", "Order-*") { Position = 2, ProcessedCount = 3 });
    await subscriptionStore.SaveAsync(new PersistentSubscription("notification-sender", "*") { Position = 3, ProcessedCount = 4 });
}

// Demo event types
record DemoOrderCreated : Catga.Abstractions.IEvent
{
    public long MessageId { get; init; } = Random.Shared.NextInt64();
    public string OrderId { get; init; } = "";
    public string CustomerId { get; init; } = "";
    public decimal Amount { get; init; }
}

record DemoOrderShipped : Catga.Abstractions.IEvent
{
    public long MessageId { get; init; } = Random.Shared.NextInt64();
    public string OrderId { get; init; } = "";
    public string TrackingNumber { get; init; } = "";
}

record DemoCustomerRegistered : Catga.Abstractions.IEvent
{
    public long MessageId { get; init; } = Random.Shared.NextInt64();
    public string CustomerId { get; init; } = "";
    public string Email { get; init; } = "";
}

// Simple no-op resilience provider for dashboard
sealed class NoOpResiliencePipelineProvider : IResiliencePipelineProvider
{
    public ValueTask<T> ExecuteMediatorAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) => action(ct);
    public ValueTask ExecuteMediatorAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct) => action(ct);
    public ValueTask<T> ExecuteTransportPublishAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) => action(ct);
    public ValueTask ExecuteTransportPublishAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct) => action(ct);
    public ValueTask<T> ExecuteTransportSendAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) => action(ct);
    public ValueTask ExecuteTransportSendAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct) => action(ct);
    public ValueTask<T> ExecutePersistenceAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) => action(ct);
    public ValueTask ExecutePersistenceAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct) => action(ct);
}
