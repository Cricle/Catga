using Catga.Abstractions;
using Catga.AspNetCore;
using Microsoft.AspNetCore.Http;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;

namespace OrderSystem.Api.Endpoints;

/// <summary>
/// Order endpoint handlers using [CatgaEndpoint] attribute.
/// Source generator automatically creates RegisterEndpoints method.
/// Zero reflection, AOT-compatible, hot-path friendly.
/// </summary>
public partial class OrderEndpointHandlers
{
    [CatgaEndpoint(HttpMethod.Post, "/api/orders", Name = "CreateOrder", Description = "Create a new order")]
    public partial async Task<IResult> CreateOrder(
        CreateOrderCommand cmd,
        ICatgaMediator mediator,
        IEventStore eventStore);

    [CatgaEndpoint(HttpMethod.Get, "/api/orders/{id}", Name = "GetOrder", Description = "Get order by ID")]
    public partial async Task<IResult> GetOrder(
        GetOrderQuery query,
        ICatgaMediator mediator);

    [CatgaEndpoint(HttpMethod.Get, "/api/orders", Name = "GetAllOrders", Description = "Get all orders")]
    public partial async Task<IResult> GetAllOrders(
        GetAllOrdersQuery query,
        ICatgaMediator mediator);

    [CatgaEndpoint(HttpMethod.Put, "/api/orders/{id}/pay", Name = "PayOrder", Description = "Pay for an order")]
    public partial async Task<IResult> PayOrder(
        PayOrderCommand cmd,
        ICatgaMediator mediator,
        IEventStore eventStore);

    [CatgaEndpoint(HttpMethod.Delete, "/api/orders/{id}", Name = "CancelOrder", Description = "Cancel an order")]
    public partial async Task<IResult> CancelOrder(
        CancelOrderCommand cmd,
        ICatgaMediator mediator,
        IEventStore eventStore);
}

/// <summary>
/// User implementation of partial methods.
/// This is where the actual business logic is implemented.
/// </summary>
public partial class OrderEndpointHandlers
{
    public partial async Task<IResult> CreateOrder(
        CreateOrderCommand cmd,
        ICatgaMediator mediator,
        IEventStore eventStore)
    {
        var result = await mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(cmd);

        if (!result.IsSuccess)
            return Results.BadRequest(new { error = result.Error });

        // Publish order created event
        await eventStore.AppendAsync("orders", new IEvent[]
        {
            new OrderCreatedEvent
            {
                OrderId = result.Value.OrderId,
                CustomerId = cmd.CustomerId,
                TotalAmount = cmd.Items.Sum(i => i.Subtotal),
                CreatedAt = DateTime.UtcNow
            }
        }, 0);

        return Results.Created($"/api/orders/{result.Value.OrderId}", result.Value);
    }

    public partial async Task<IResult> GetOrder(
        GetOrderQuery query,
        ICatgaMediator mediator)
    {
        var result = await mediator.SendAsync<GetOrderQuery, Order?>(query);

        if (!result.IsSuccess || result.Value == null)
            return Results.NotFound();

        return Results.Ok(result.Value);
    }

    public partial async Task<IResult> GetAllOrders(
        GetAllOrdersQuery query,
        ICatgaMediator mediator)
    {
        var result = await mediator.SendAsync<GetAllOrdersQuery, List<Order>>(query);

        if (!result.IsSuccess)
            return Results.BadRequest(result.Error);

        return Results.Ok(result.Value);
    }

    public partial async Task<IResult> PayOrder(
        PayOrderCommand cmd,
        ICatgaMediator mediator,
        IEventStore eventStore)
    {
        var result = await mediator.SendAsync<PayOrderCommand, bool>(cmd);

        if (!result.IsSuccess)
            return Results.BadRequest(new { error = result.Error });

        // Publish order paid event
        await eventStore.AppendAsync("orders", new IEvent[]
        {
            new OrderPaidEvent
            {
                OrderId = cmd.OrderId,
                PaidAt = DateTime.UtcNow
            }
        }, 0);

        return Results.Ok(new { message = "Order paid successfully" });
    }

    public partial async Task<IResult> CancelOrder(
        CancelOrderCommand cmd,
        ICatgaMediator mediator,
        IEventStore eventStore)
    {
        var result = await mediator.SendAsync<CancelOrderCommand, bool>(cmd);

        if (!result.IsSuccess)
            return Results.BadRequest(new { error = result.Error });

        // Publish order cancelled event
        await eventStore.AppendAsync("orders", new IEvent[]
        {
            new OrderCancelledEvent
            {
                OrderId = cmd.OrderId,
                CancelledAt = DateTime.UtcNow
            }
        }, 0);

        return Results.NoContent();
    }
}
