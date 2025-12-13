using Catga;
using Microsoft.AspNetCore.Mvc;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;

namespace OrderSystem.Api.Endpoints;

/// <summary>
/// Order API endpoints using Minimal API pattern.
/// Demonstrates clean separation of endpoint definitions from Program.cs.
/// </summary>
public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders")
            .WithTags("Orders")
            ;

        group.MapPost("/", CreateOrder)
            .WithName("CreateOrder")
            .WithSummary("Create a new order")
            .WithDescription("Creates a new order and publishes OrderCreatedEvent")
            .Produces<OrderCreatedResult>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/flow", CreateOrderWithFlow)
            .WithName("CreateOrderWithFlow")
            .WithSummary("Create order using Flow/Saga pattern")
            .WithDescription("Creates order with multi-step flow including compensation on failure")
            .Produces<OrderCreatedResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{orderId}", GetOrder)
            .WithName("GetOrder")
            .WithSummary("Get order by ID")
            .Produces<Order>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{orderId}/cancel", CancelOrder)
            .WithName("CancelOrder")
            .WithSummary("Cancel an existing order")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/customer/{customerId}", GetCustomerOrders)
            .WithName("GetCustomerOrders")
            .WithSummary("Get all orders for a customer")
            .Produces<List<Order>>(StatusCodes.Status200OK);

        return app;
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
}

/// <summary>Request body for cancel order endpoint</summary>
public record CancelOrderRequest(string? Reason);
