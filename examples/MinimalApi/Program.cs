using Catga;
using Catga.Core;
using Catga.Handlers;
using Catga.Messages;

var builder = WebApplication.CreateBuilder(args);

// Register Catga with InMemory transport and persistence
builder.Services
    .AddCatga()
    .AddInMemoryTransport()
    .AddInMemoryEventStore();

var app = builder.Build();

// Create Order endpoint
app.MapPost("/orders", async (CreateOrderCommand command, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync(command);

    if (result.IsSuccess)
    {
        await mediator.PublishAsync(new OrderCreatedEvent
        {
            OrderId = result.Value.Id,
            CustomerId = result.Value.CustomerId,
            Amount = result.Value.Amount
        });

        return Results.Ok(result.Value);
    }

    return Results.BadRequest(result.Error?.Message);
});

// Get Order status endpoint
app.MapGet("/orders/{id}", (string id) =>
    Results.Ok(new { OrderId = id, Status = "Pending" }));

app.Run();

// ===== Messages =====

public record CreateOrderCommand : IRequest<Order>
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public required string CustomerId { get; init; }
    public required decimal Amount { get; init; }
}

public record OrderCreatedEvent : IEvent
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public required string OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required decimal Amount { get; init; }
}

public record Order
{
    public required string Id { get; init; }
    public required string CustomerId { get; init; }
    public required decimal Amount { get; init; }
}

// ===== Handlers =====

public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Order>
{
    private readonly ILogger<CreateOrderCommandHandler> _logger;

    public CreateOrderCommandHandler(ILogger<CreateOrderCommandHandler> logger)
    {
        _logger = logger;
    }

    public Task<CatgaResult<Order>> HandleAsync(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating order for customer {CustomerId}", request.CustomerId);

        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            CustomerId = request.CustomerId,
            Amount = request.Amount
        };

        return Task.FromResult(CatgaResult<Order>.Success(order));
    }
}

public class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<OrderCreatedEventHandler> _logger;

    public OrderCreatedEventHandler(ILogger<OrderCreatedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Order created: {OrderId}, Customer: {CustomerId}, Amount: {Amount}",
            @event.OrderId,
            @event.CustomerId,
            @event.Amount);

        return Task.CompletedTask;
    }
}

