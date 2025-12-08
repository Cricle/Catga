using Catga;
using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.InMemory.Stores;
using Catga.Persistence.Stores;
using OrderSystem.Api.Domain;
using OrderSystem.Api.EventSourcing;
using OrderSystem.Api.Messages;

namespace OrderSystem.Api.Extensions;

/// <summary>
/// Extension methods for endpoint registration.
/// </summary>
public static class EndpointExtensions
{
    /// <summary>
    /// Map core order endpoints.
    /// </summary>
    public static WebApplication MapOrderEndpoints(this WebApplication app)
    {
        app.MapPost("/api/orders", async (CreateOrderCommand cmd, ICatgaMediator mediator) =>
        {
            var result = await mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(cmd);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

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

        return app;
    }

    /// <summary>
    /// Map time travel endpoints.
    /// </summary>
    public static WebApplication MapTimeTravelEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/timetravel").WithTags("Time Travel");

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
                new OrderItemAdded { MessageId = msgId++, OrderId = orderId, ProductName = "Keyboard", Quantity = 1, Price = 79.99m },
                new OrderStatusChanged { MessageId = msgId++, OrderId = orderId, NewStatus = "Confirmed" }
            };

            await eventStore.AppendAsync(streamId, events);
            return Results.Ok(new { orderId, eventCount = events.Length, message = "Demo order created with 7 events" });
        });

        group.MapGet("/orders/{orderId}/version/{version:long}", async (
            string orderId, long version, ITimeTravelService<OrderAggregate> timeTravelService) =>
        {
            var state = await timeTravelService.GetStateAtVersionAsync(orderId, version);
            if (state == null) return Results.NotFound(new { error = "Order not found or version does not exist" });
            return Results.Ok(new
            {
                orderId = state.Id, customerId = state.CustomerId, items = state.Items,
                totalAmount = state.TotalAmount, status = state.Status, version = state.Version
            });
        });

        group.MapGet("/orders/{orderId}/history", async (
            string orderId, ITimeTravelService<OrderAggregate> timeTravelService) =>
        {
            var history = await timeTravelService.GetVersionHistoryAsync(orderId);
            return Results.Ok(history.Select(h => new { version = h.Version, eventType = h.EventType, timestamp = h.Timestamp }));
        });

        group.MapGet("/orders/{orderId}/compare/{fromVersion:long}/{toVersion:long}", async (
            string orderId, long fromVersion, long toVersion, ITimeTravelService<OrderAggregate> timeTravelService) =>
        {
            var comparison = await timeTravelService.CompareVersionsAsync(orderId, fromVersion, toVersion);
            return Results.Ok(new
            {
                fromVersion, toVersion,
                fromState = comparison.FromState == null ? null : new { comparison.FromState.TotalAmount, comparison.FromState.Status, itemCount = comparison.FromState.Items.Count },
                toState = comparison.ToState == null ? null : new { comparison.ToState.TotalAmount, comparison.ToState.Status, itemCount = comparison.ToState.Items.Count },
                eventsBetween = comparison.EventsBetween.Select(e => new { e.Version, e.EventType, e.Timestamp })
            });
        });

        return app;
    }

    /// <summary>
    /// Map projection endpoints.
    /// </summary>
    public static WebApplication MapProjectionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/projections").WithTags("Projections");

        group.MapGet("/order-summary", (OrderSummaryProjection projection) =>
            Results.Ok(new { projection.TotalOrders, projection.TotalRevenue, projection.OrdersByStatus, orders = projection.Orders.Values.Take(10) }));

        group.MapPost("/order-summary/rebuild", async (
            OrderSummaryProjection projection, InMemoryProjectionCheckpointStore checkpointStore, IEventStore eventStore) =>
        {
            var rebuilder = new ProjectionRebuilder<OrderSummaryProjection>(eventStore, checkpointStore, projection, projection.Name);
            await rebuilder.RebuildAsync();
            return Results.Ok(new { message = "Projection rebuilt", projection.TotalOrders });
        });

        group.MapGet("/customer-stats", (CustomerStatsProjection projection) => Results.Ok(projection.Stats.Values));

        return app;
    }

    /// <summary>
    /// Map subscription endpoints.
    /// </summary>
    public static WebApplication MapSubscriptionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/subscriptions").WithTags("Subscriptions");

        group.MapGet("/", async (InMemorySubscriptionStore store) =>
        {
            var subs = await store.ListAsync();
            return Results.Ok(subs.Select(s => new { s.Name, s.StreamPattern, s.Position, s.ProcessedCount, s.LastProcessedAt }));
        });

        group.MapPost("/", async (string name, string pattern, InMemorySubscriptionStore store) =>
        {
            var sub = new PersistentSubscription(name, pattern);
            await store.SaveAsync(sub);
            return Results.Created($"/api/subscriptions/{name}", new { name, pattern });
        });

        group.MapPost("/{name}/process", async (
            string name, InMemorySubscriptionStore store, IEventStore eventStore, OrderEventSubscriptionHandler handler) =>
        {
            var runner = new SubscriptionRunner(eventStore, store, handler);
            await runner.RunOnceAsync(name);
            var sub = await store.LoadAsync(name);
            return Results.Ok(new { name, processedCount = sub?.ProcessedCount ?? 0 });
        });

        return app;
    }

    /// <summary>
    /// Map audit endpoints.
    /// </summary>
    public static WebApplication MapAuditEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/audit").WithTags("Audit & Compliance");

        group.MapGet("/logs/{streamId}", async (string streamId, OrderAuditService auditService) =>
            Results.Ok(await auditService.GetLogsAsync(streamId)));

        group.MapPost("/verify/{streamId}", async (string streamId, OrderAuditService auditService) =>
        {
            var result = await auditService.VerifyStreamAsync(streamId);
            return Results.Ok(new { streamId, result.IsValid, result.Hash, result.Error });
        });

        group.MapPost("/gdpr/erasure-request", async (string customerId, string requestedBy, OrderAuditService auditService) =>
        {
            await auditService.RequestCustomerErasureAsync(customerId, requestedBy);
            return Results.Accepted();
        });

        group.MapGet("/gdpr/pending-requests", async (OrderAuditService auditService) =>
            Results.Ok(await auditService.GetPendingErasureRequestsAsync()));

        return app;
    }

    /// <summary>
    /// Map snapshot endpoints.
    /// </summary>
    public static WebApplication MapSnapshotEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/snapshots").WithTags("Snapshots");

        group.MapPost("/orders/{orderId}", async (string orderId, IEnhancedSnapshotStore snapshotStore, IEventStore eventStore) =>
        {
            var streamId = $"OrderAggregate-{orderId}";
            var eventStream = await eventStore.ReadAsync(streamId);
            if (eventStream.Events.Count == 0) return Results.NotFound();

            var aggregate = new OrderAggregate();
            foreach (var stored in eventStream.Events) aggregate.Apply(stored.Event);

            await snapshotStore.SaveAsync(streamId, aggregate, eventStream.Version);
            return Results.Ok(new { streamId, version = eventStream.Version, message = "Snapshot created" });
        });

        group.MapGet("/orders/{orderId}/history", async (string orderId, IEnhancedSnapshotStore snapshotStore) =>
        {
            var streamId = $"OrderAggregate-{orderId}";
            return Results.Ok(await snapshotStore.GetSnapshotHistoryAsync(streamId));
        });

        group.MapGet("/orders/{orderId}/version/{version:long}", async (string orderId, long version, IEnhancedSnapshotStore snapshotStore) =>
        {
            var streamId = $"OrderAggregate-{orderId}";
            var snapshot = await snapshotStore.LoadAtVersionAsync<OrderAggregate>(streamId, version);
            if (!snapshot.HasValue) return Results.NotFound();
            return Results.Ok(new { version = snapshot.Value.Version, state = new { snapshot.Value.State.Id, snapshot.Value.State.CustomerId, snapshot.Value.State.TotalAmount, snapshot.Value.State.Status } });
        });

        return app;
    }
}
