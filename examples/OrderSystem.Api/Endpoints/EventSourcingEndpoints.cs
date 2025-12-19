using Catga.Abstractions;
using Catga.EventSourcing;
using OrderSystem.Api;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Endpoints;

/// <summary>
/// Event Sourcing related endpoints: Time Travel, Projections, Subscriptions.
/// </summary>
public static class EventSourcingEndpoints
{
    public static IEndpointRouteBuilder MapEventSourcingEndpoints(this IEndpointRouteBuilder app)
    {
        MapTimeTravelEndpoints(app);
        MapProjectionEndpoints(app);
        MapSubscriptionEndpoints(app);
        MapSnapshotEndpoints(app);

        return app;
    }

    private static void MapTimeTravelEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/timetravel")
            .WithTags("Time Travel");

        group.MapPost("/demo/create", async (IEventStore eventStore) =>
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

            await eventStore.AppendAsync(streamId, events);
            return Results.Ok(new TimeTravelDemoResponse(orderId, streamId, events.Length));
        })
        .WithName("CreateTimeTravelDemo")
        .WithSummary("Create demo order with multiple events for time travel");

        group.MapGet("/orders/{orderId}/version/{version:long}", async (
            string orderId,
            long version,
            ITimeTravelService<OrderAggregate> timeTravel) =>
        {
            var state = await timeTravel.GetStateAtVersionAsync(orderId, version);
            return state == null
                ? Results.NotFound()
                : Results.Ok(state);
        })
        .WithName("GetOrderAtVersion")
        .WithSummary("Get order state at a specific version");

        group.MapGet("/orders/{orderId}/history", async (
            string orderId,
            ITimeTravelService<OrderAggregate> timeTravel) =>
        {
            var history = await timeTravel.GetVersionHistoryAsync(orderId);
            return Results.Ok(history);
        })
        .WithName("GetOrderHistory")
        .WithSummary("Get complete version history for an order");
    }

    private static void MapProjectionEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projections")
            .WithTags("Projections");

        group.MapGet("/order-summary", (OrderSummaryProjection projection) =>
            Results.Ok(projection))
        .WithName("GetOrderSummary")
        .WithSummary("Get aggregated order statistics");

        group.MapPost("/order-summary/rebuild", async (
            OrderSummaryProjection projection,
            IProjectionCheckpointStore checkpointStore,
            IEventStore eventStore) =>
        {
            var rebuilder = new ProjectionRebuilder<OrderSummaryProjection>(
                eventStore, checkpointStore, projection, projection.Name);
            await rebuilder.RebuildAsync();
            return Results.Ok(new ProjectionRebuildResponse2("Projection rebuilt", projection.TotalOrders));
        })
        .WithName("RebuildOrderSummary")
        .WithSummary("Rebuild order summary projection from events");

        group.MapGet("/customer-stats", (CustomerStatsProjection projection) =>
            Results.Ok(projection.Stats.Values))
        .WithName("GetCustomerStats")
        .WithSummary("Get customer statistics");
    }

    private static void MapSubscriptionEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/subscriptions")
            .WithTags("Subscriptions");

        group.MapGet("/", async (ISubscriptionStore store) =>
            Results.Ok(await store.ListAsync()))
        .WithName("ListSubscriptions")
        .WithSummary("List all persistent subscriptions");

        group.MapPost("/", async (CreateSubscriptionRequest request, ISubscriptionStore store) =>
        {
            await store.SaveAsync(new PersistentSubscription(request.Name, request.Pattern));
            return Results.Created($"/api/subscriptions/{request.Name}", new SubscriptionCreatedResponse2(request.Name, request.Pattern));
        })
        .WithName("CreateSubscription")
        .WithSummary("Create a new persistent subscription");

        group.MapPost("/{name}/process", async (
            string name,
            ISubscriptionStore store,
            IEventStore eventStore,
            OrderEventSubscriptionHandler handler) =>
        {
            var runner = new SubscriptionRunner(eventStore, store, handler);
            await runner.RunOnceAsync(name);
            var subscription = await store.LoadAsync(name);
            return Results.Ok(new SubscriptionProcessedResponse2(name, subscription?.ProcessedCount ?? 0));
        })
        .WithName("ProcessSubscription")
        .WithSummary("Process events for a subscription");
    }

    private static void MapSnapshotEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/snapshots")
            .WithTags("Snapshots");

        group.MapPost("/orders/{orderId}", async (
            string orderId,
            IEnhancedSnapshotStore snapshotStore,
            IEventStore eventStore) =>
        {
            var streamId = $"OrderAggregate-{orderId}";
            var stream = await eventStore.ReadAsync(streamId);

            if (stream.Events.Count == 0)
                return Results.NotFound();

            var aggregate = new OrderAggregate();
            foreach (var e in stream.Events)
                aggregate.Apply(e.Event);

            await snapshotStore.SaveAsync(streamId, aggregate, stream.Version);
            return Results.Ok(new SnapshotCreatedResponse2(streamId, stream.Version));
        })
        .WithName("CreateOrderSnapshot")
        .WithSummary("Create a snapshot for an order aggregate");

        group.MapGet("/orders/{orderId}/history", async (string orderId, IEnhancedSnapshotStore snapshotStore) =>
            Results.Ok(await snapshotStore.GetSnapshotHistoryAsync($"OrderAggregate-{orderId}")))
        .WithName("GetSnapshotHistory")
        .WithSummary("Get snapshot history for an order");
    }
}

public record CreateSubscriptionRequest(string Name, string Pattern);
