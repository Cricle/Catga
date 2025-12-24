using Catga;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OrderSystem.Commands;
using OrderSystem.Configuration;
using OrderSystem.Dtos;
using OrderSystem.Models;
using OrderSystem.Queries;

namespace OrderSystem.Extensions;

public static class EndpointExtensions
{
    public static void MapOrderSystemEndpoints(this WebApplication app)
    {
        // System info endpoint
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

        // Health check endpoints
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready")
        });
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live")
        });

        // Order management endpoints
        app.MapPost("/orders", async (CreateOrderRequest req, ICatgaMediator mediator) =>
        {
            var result = await mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(
                new CreateOrderCommand(req.CustomerId, req.Items));
            return result.IsSuccess
                ? Results.Created($"/orders/{result.Value!.OrderId}", result.Value)
                : Results.BadRequest(result.Error);
        });

        app.MapGet("/orders/{id}", async (string id, ICatgaMediator mediator) =>
        {
            var result = await mediator.SendAsync<GetOrderQuery, Order?>(new GetOrderQuery(id));
            return result.IsSuccess && result.Value != null
                ? Results.Ok(result.Value)
                : Results.NotFound();
        });

        app.MapGet("/orders", async (ICatgaMediator mediator) =>
        {
            var result = await mediator.SendAsync<GetAllOrdersQuery, List<Order>>(new GetAllOrdersQuery());
            return Results.Ok(result.Value ?? new List<Order>());
        });

        app.MapPost("/orders/{id}/pay", async (string id, ICatgaMediator mediator, OrderStore store) =>
        {
            var result = await mediator.SendAsync<PayOrderCommand>(new PayOrderCommand(id, "default"));
            if (!result.IsSuccess) return Results.BadRequest(result.Error);

            var order = store.Get(id);
            return order != null ? Results.Ok(order) : Results.NotFound();
        });

        app.MapPost("/orders/{id}/ship", async (string id, ICatgaMediator mediator, OrderStore store) =>
        {
            var result = await mediator.SendAsync<ShipOrderCommand>(
                new ShipOrderCommand(id, "TRACK-" + Guid.NewGuid().ToString("N")[..8]));
            if (!result.IsSuccess) return Results.BadRequest(result.Error);

            var order = store.Get(id);
            return order != null ? Results.Ok(order) : Results.NotFound();
        });

        app.MapPost("/orders/{id}/cancel", async (string id, ICatgaMediator mediator, OrderStore store) =>
        {
            var result = await mediator.SendAsync<CancelOrderCommand>(new CancelOrderCommand(id));
            if (!result.IsSuccess) return Results.BadRequest(result.Error);

            var order = store.Get(id);
            return order != null ? Results.Ok(order) : Results.NotFound();
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
            var byStatus = orders.GroupBy(o => o.Status)
                .ToDictionary(g => g.Key.ToString(), g => g.Count());
            var totalRevenue = orders.Where(o => o.Status != OrderStatus.Cancelled)
                .Sum(o => o.Total);

            return Results.Ok(new StatsResponse(
                TotalOrders: orders.Count,
                ByStatus: byStatus,
                TotalRevenue: totalRevenue,
                Timestamp: DateTime.UtcNow
            ));
        });
    }

    public static void PrintStartupBanner(string nodeId, bool isCluster, int port, string transport, string persistence)
    {
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
║   GET  /health              - Health check (all)             ║
║   GET  /health/ready        - Readiness probe                ║
║   GET  /health/live         - Liveness probe                 ║
║   GET  /stats               - Statistics                     ║
║   POST /orders              - Create order                   ║
║   GET  /orders              - List orders                    ║
║   GET  /orders/{{id}}         - Get order                      ║
║   POST /orders/{{id}}/pay     - Pay order                      ║
║   POST /orders/{{id}}/ship    - Ship order                     ║
║   POST /orders/{{id}}/cancel  - Cancel order                   ║
║   GET  /orders/{{id}}/history - Event history                  ║
╠══════════════════════════════════════════════════════════════╣
║ Hosted Services:                                             ║
║   ✓ RecoveryHostedService   - Auto health check & recovery  ║
║   ✓ TransportHostedService  - Lifecycle management          ║
║   ✓ OutboxProcessorService  - Background message processing ║
╚══════════════════════════════════════════════════════════════╝
");
    }
}
