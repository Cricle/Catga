using System.Diagnostics.CodeAnalysis;
using Catga;
using Microsoft.AspNetCore.Mvc;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;

namespace OrderSystem.Api.Endpoints;

/// <summary>
/// Order API endpoints - Full order lifecycle management.
/// Lifecycle: Pending → Paid → Processing → Shipped → Delivered
/// </summary>
public static class OrderEndpoints
{
    [RequiresDynamicCode("Uses reflection for endpoint mapping")]
    [RequiresUnreferencedCode("Uses reflection for endpoint mapping")]
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders")
            .WithTags("Orders");

        // ===== Queries =====
        group.MapGet("/", GetAllOrders)
            .WithName("GetAllOrders")
            .WithSummary("Get all orders with optional status filter");

        group.MapGet("/stats", GetOrderStats)
            .WithName("GetOrderStats")
            .WithSummary("Get order statistics dashboard");

        group.MapGet("/{orderId}", GetOrder)
            .WithName("GetOrder")
            .WithSummary("Get order by ID");

        group.MapGet("/customer/{customerId}", GetCustomerOrders)
            .WithName("GetCustomerOrders")
            .WithSummary("Get all orders for a customer");

        // ===== Commands - Order Creation =====
        group.MapPost("/", CreateOrder)
            .WithName("CreateOrder")
            .WithSummary("Create a new order (status: Pending)");

        group.MapPost("/flow", CreateOrderWithFlow)
            .WithName("CreateOrderWithFlow")
            .WithSummary("Create order using Flow/Saga pattern with compensation");

        // ===== Commands - Order Lifecycle =====
        group.MapPost("/{orderId}/pay", PayOrder)
            .WithName("PayOrder")
            .WithSummary("Pay for an order (Pending → Paid)");

        group.MapPost("/{orderId}/process", ProcessOrder)
            .WithName("ProcessOrder")
            .WithSummary("Start processing (Paid → Processing)");

        group.MapPost("/{orderId}/ship", ShipOrder)
            .WithName("ShipOrder")
            .WithSummary("Ship the order (Processing → Shipped)");

        group.MapPost("/{orderId}/deliver", DeliverOrder)
            .WithName("DeliverOrder")
            .WithSummary("Mark as delivered (Shipped → Delivered)");

        group.MapPost("/{orderId}/cancel", CancelOrder)
            .WithName("CancelOrder")
            .WithSummary("Cancel an order");

        return app;
    }

    // ===== Query Handlers =====

    private static async Task<IResult> GetAllOrders(
        [FromQuery] OrderStatus? status,
        [FromQuery] int limit = 100,
        ICatgaMediator mediator = default!,
        CancellationToken ct = default)
    {
        var result = await mediator.SendAsync<GetAllOrdersQuery, List<Order>>(
            new(status, limit > 0 ? limit : 100), ct);
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> GetOrderStats(
        ICatgaMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.SendAsync<GetOrderStatsQuery, OrderStats>(new(), ct);
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> CreateOrder(
        CreateOrderCommand command,
        ICatgaMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(command, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.BadRequest(new ProblemDetails { Detail = result.Error });
    }

    private static async Task<IResult> CreateOrderWithFlow(
        CreateOrderFlowCommand command,
        ICatgaMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.SendAsync<CreateOrderFlowCommand, OrderCreatedResult>(command, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.BadRequest(new ProblemDetails { Detail = result.Error });
    }

    private static async Task<IResult> GetOrder(
        string orderId,
        ICatgaMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.SendAsync<GetOrderQuery, Order?>(new(orderId), ct);
        return result.Value is null
            ? Results.NotFound()
            : Results.Ok(result.Value);
    }

    private static async Task<IResult> CancelOrder(
        string orderId,
        CancelOrderRequest? request,
        ICatgaMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.SendAsync(new CancelOrderCommand(orderId, request?.Reason), ct);
        return result.IsSuccess
            ? Results.Ok()
            : Results.BadRequest(new ProblemDetails { Detail = result.Error });
    }

    private static async Task<IResult> GetCustomerOrders(
        string customerId,
        ICatgaMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.SendAsync<GetUserOrdersQuery, List<Order>>(new(customerId), ct);
        return Results.Ok(result.Value);
    }

    // ===== Lifecycle Commands =====

    private static async Task<IResult> PayOrder(
        string orderId,
        PayOrderRequest request,
        ICatgaMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.SendAsync(
            new PayOrderCommand(orderId, request.PaymentMethod, request.TransactionId), ct);
        return result.IsSuccess
            ? Results.Ok(new SuccessResponse("Payment successful", orderId))
            : Results.BadRequest(new ProblemDetails { Detail = result.Error });
    }

    private static async Task<IResult> ProcessOrder(
        string orderId,
        ICatgaMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.SendAsync(new ProcessOrderCommand(orderId), ct);
        return result.IsSuccess
            ? Results.Ok(new SuccessResponse("Order is now processing", orderId))
            : Results.BadRequest(new ProblemDetails { Detail = result.Error });
    }

    private static async Task<IResult> ShipOrder(
        string orderId,
        ShipOrderRequest request,
        ICatgaMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.SendAsync(
            new ShipOrderCommand(orderId, request.TrackingNumber), ct);
        return result.IsSuccess
            ? Results.Ok(new ShipResponse("Order shipped", orderId, request.TrackingNumber))
            : Results.BadRequest(new ProblemDetails { Detail = result.Error });
    }

    private static async Task<IResult> DeliverOrder(
        string orderId,
        ICatgaMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.SendAsync(new DeliverOrderCommand(orderId), ct);
        return result.IsSuccess
            ? Results.Ok(new SuccessResponse("Order delivered", orderId))
            : Results.BadRequest(new ProblemDetails { Detail = result.Error });
    }
}

// ===== Request DTOs =====
public record CancelOrderRequest(string? Reason);
public record PayOrderRequest(string PaymentMethod, string? TransactionId = null);
public record ShipOrderRequest(string TrackingNumber);
