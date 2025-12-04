using Catga;
using Catga.DependencyInjection;
using Catga.Flow;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// Catga setup - just 3 lines
builder.Services
    .AddCatga()
    .UseMemoryPack()
    .ForDevelopment();

builder.Services.AddInMemoryTransport();
builder.Services.AddInMemoryPersistence();

// Simple in-memory store
builder.Services.AddSingleton<OrderStore>();

var app = builder.Build();

// Create order with Flow (auto-compensation on failure)
app.MapPost("/orders", async (CreateOrderRequest req, ICatgaMediator m, OrderStore store) =>
{
    var orderId = Guid.NewGuid().ToString("N")[..8];

    var result = await Flow.Create("CreateOrder")
        .Step(
            () => store.Save(new Order(orderId, req.CustomerId, req.Amount)),
            () => store.Delete(orderId))
        .Step(
            () => SimulatePayment(req.PaymentMethod, req.Amount),
            () => SimulateRefund(orderId))
        .ExecuteAsync();

    return result.IsSuccess
        ? Results.Ok(new { orderId, message = "Order created" })
        : Results.BadRequest(new { error = result.Error });
});

// Get order
app.MapGet("/orders/{id}", (string id, OrderStore store) =>
    store.Get(id) is { } order ? Results.Ok(order) : Results.NotFound());

app.Run();

// Simulated payment
static Task SimulatePayment(string method, decimal amount)
{
    if (method.StartsWith("FAIL")) throw new Exception("Payment declined");
    return Task.CompletedTask;
}

static Task SimulateRefund(string orderId)
{
    Console.WriteLine($"Refunded order {orderId}");
    return Task.CompletedTask;
}

// Simple types
record CreateOrderRequest(string CustomerId, decimal Amount, string PaymentMethod = "Card");
record Order(string Id, string CustomerId, decimal Amount);

class OrderStore
{
    private readonly ConcurrentDictionary<string, Order> _orders = new();
    public void Save(Order order) => _orders[order.Id] = order;
    public void Delete(string id) => _orders.TryRemove(id, out _);
    public Order? Get(string id) => _orders.GetValueOrDefault(id);
}
