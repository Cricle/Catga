using Catga;
using Catga.AspNetCore;
using Catga.DependencyInjection;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Catga setup - framework handles telemetry, resilience, etc.
builder.Services
    .AddCatga()
    .UseMemoryPack()
    .WithTracing()
    .WithLogging()
    .ForDevelopment();

builder.Services.AddInMemoryTransport();
builder.Services.AddInMemoryPersistence();

// Business services only - no infrastructure code
builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
builder.Services.AddSingleton<IInventoryService, DistributedInventoryService>();
builder.Services.AddSingleton<IPaymentService, SimulatedPaymentService>();

// Auto-register handlers (source generated)
builder.Services.AddGeneratedHandlers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// API endpoints - minimal routing
app.MapCatgaRequest<CreateOrderFlowCommand, OrderCreatedResult>("/api/orders")
    .WithName("CreateOrder").WithTags("Orders");

app.MapCatgaQuery<GetOrderQuery, Order?>("/api/orders/{orderId}")
    .WithName("GetOrder").WithTags("Orders");

app.MapPost("/api/orders/{orderId}/cancel", async (string orderId, string? reason, ICatgaMediator m) =>
{
    var result = await m.SendAsync(new CancelOrderCommand(orderId, reason));
    return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
}).WithName("CancelOrder").WithTags("Orders");

// Demo endpoint
app.MapPost("/demo/order", async (ICatgaMediator m) =>
{
    var items = new List<OrderItem>
    {
        new() { ProductId = "PROD-001", ProductName = "iPhone 15", Quantity = 1, UnitPrice = 5999m },
        new() { ProductId = "PROD-002", ProductName = "AirPods Pro", Quantity = 2, UnitPrice = 1999m }
    };

    var result = await m.SendAsync<CreateOrderFlowCommand, OrderCreatedResult>(
        new("DEMO-CUST-001", items, "123 Demo Street", "Alipay"));

    return Results.Ok(new
    {
        result.IsSuccess,
        OrderId = result.Value?.OrderId,
        TotalAmount = result.Value?.TotalAmount,
        Error = result.Error
    });
}).WithName("DemoOrder").WithTags("Demo");

app.Run();

public partial class Program { }
