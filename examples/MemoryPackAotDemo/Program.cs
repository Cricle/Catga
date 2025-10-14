using Catga;
using Catga.DependencyInjection;
using Catga.Messages;
using MemoryPack;

var builder = WebApplication.CreateSlimBuilder(args);

// ✅ Catga + MemoryPack (100% AOT compatible)
builder.Services.AddCatga()
    .UseMemoryPack()
    .ForProduction();

var app = builder.Build();

// ✅ Simple health check endpoint
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Time = DateTime.UtcNow }));

// ✅ Create order endpoint
app.MapPost("/orders", async (CreateOrderRequest request, ICatgaMediator mediator) =>
{
    var command = new CreateOrder(request.OrderId, request.Amount);
    var result = await mediator.SendAsync<CreateOrder, OrderResult>(command);

    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { Error = result.ErrorMessage });
});

// ✅ Get order endpoint (demo query)
app.MapGet("/orders/{orderId}", async (string orderId, ICatgaMediator mediator) =>
{
    var query = new GetOrder(orderId);
    var result = await mediator.SendAsync<GetOrder, OrderResult>(query);

    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.NotFound(new { Error = result.ErrorMessage });
});

app.Run();

// ✅ Messages (AOT-compatible with MemoryPack)
[MemoryPackable]
public partial record CreateOrder(string OrderId, decimal Amount) : IRequest<OrderResult>;

[MemoryPackable]
public partial record GetOrder(string OrderId) : IRequest<OrderResult>;

[MemoryPackable]
public partial record OrderResult(string OrderId, string Status, decimal Amount);

// ✅ Request DTO (for HTTP endpoint)
public record CreateOrderRequest(string OrderId, decimal Amount);

// ✅ Handlers
public sealed class CreateOrderHandler : IRequestHandler<CreateOrder, OrderResult>
{
    public ValueTask<CatgaResult<OrderResult>> HandleAsync(
        CreateOrder request,
        CancellationToken cancellationToken = default)
    {
        // Validate
        if (request.Amount <= 0)
            return ValueTask.FromResult(
                CatgaResult<OrderResult>.Failure("Amount must be positive"));

        // Process (in-memory for demo)
        var result = new OrderResult(request.OrderId, "Created", request.Amount);
        return ValueTask.FromResult(CatgaResult<OrderResult>.Success(result));
    }
}

public sealed class GetOrderHandler : IRequestHandler<GetOrder, OrderResult>
{
    public ValueTask<CatgaResult<OrderResult>> HandleAsync(
        GetOrder request,
        CancellationToken cancellationToken = default)
    {
        // Demo: return mock data
        var result = new OrderResult(request.OrderId, "Pending", 99.99m);
        return ValueTask.FromResult(CatgaResult<OrderResult>.Success(result));
    }
}

