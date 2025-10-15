using Catga;
using Catga.AspNetCore;
using Catga.DependencyInjection;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ===== Aspire Service Defaults =====
builder.AddServiceDefaults();  // OpenTelemetry, Health Checks, Service Discovery

// ===== Catga Configuration =====
builder.Services.AddCatga()                      // Add Catga core services
    .UseMemoryPack()                             // Serializer (AOT-friendly)
    .WithDebug()                                 // Enable native debugging (dev only)
    .ForDevelopment();                           // Development environment

builder.Services.AddInMemoryTransport();         // Transport layer (replaceable with NATS)

// Graceful lifecycle (using CatgaBuilder)
builder.Services.AddCatgaBuilder(b => b.UseGracefulLifecycle());

// Auto-register all handlers and services (Source Generator)
builder.Services.AddGeneratedHandlers();   // Handlers
builder.Services.AddGeneratedServices();   // Services (Repository, etc.)

// ===== No manual service registration needed! =====
// Source Generator auto-discovers and registers:
// - IOrderRepository → InMemoryOrderRepository
// - IInventoryService → MockInventoryService
// - IPaymentService → MockPaymentService

// ===== ASP.NET Core Configuration =====
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ===== Aspire Default Endpoints =====
app.MapDefaultEndpoints();  // /health, /health/live, /health/ready

// ===== Middleware Configuration =====
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ===== API Endpoints =====
var mediator = app.Services.GetRequiredService<ICatgaMediator>();

// Create order
app.MapCatgaRequest<CreateOrderCommand, OrderCreatedResult>("/api/orders")
    .WithName("CreateOrder")
    .WithTags("Orders");

// Confirm order
app.MapPost("/api/orders/confirm", async (ConfirmOrderCommand cmd, ICatgaMediator m) =>
{
    var result = await m.SendAsync(cmd);
    return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
}).WithName("ConfirmOrder").WithTags("Orders");

// Pay order
app.MapPost("/api/orders/pay", async (PayOrderCommand cmd, ICatgaMediator m) =>
{
    var result = await m.SendAsync(cmd);
    return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
}).WithName("PayOrder").WithTags("Orders");

// Cancel order
app.MapPost("/api/orders/cancel", async (CancelOrderCommand cmd, ICatgaMediator m) =>
{
    var result = await m.SendAsync(cmd);
    return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
}).WithName("CancelOrder").WithTags("Orders");

// Get order
app.MapCatgaQuery<GetOrderQuery, Order?>("/api/orders/{orderId}")
    .WithName("GetOrder")
    .WithTags("Orders");

// Get customer orders
app.MapGet("/api/customers/{customerId}/orders", async (
    string customerId,
    int pageIndex,
    int pageSize,
    ICatgaMediator m) =>
{
    var query = new GetCustomerOrdersQuery(customerId, pageIndex, pageSize);
    var result = await m.SendAsync<GetCustomerOrdersQuery, List<Order>>(query);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
}).WithName("GetCustomerOrders").WithTags("Orders");

// Debug endpoints (dev only)
if (app.Environment.IsDevelopment())
{
    app.MapCatgaDebugEndpoints();  // /debug/flows, /debug/stats
}

// Health check
app.MapGet("/health", () => Results.Ok(new
{
    Status = "Healthy",
    Timestamp = DateTime.UtcNow,
    Service = "OrderSystem.Api"
}));

// Test endpoint: create sample order
app.MapPost("/test/create-order", async (ICatgaMediator mediator) =>
{
    var command = new CreateOrderCommand(
        CustomerId: "CUST-001",
        Items: new List<OrderItem>
        {
            new() { ProductId = "PROD-001", ProductName = "Product A", Quantity = 2, UnitPrice = 99.99m },
            new() { ProductId = "PROD-002", ProductName = "Product B", Quantity = 1, UnitPrice = 199.99m }
        },
        ShippingAddress: "123 Main St, Beijing",
        PaymentMethod: "Alipay"
    );

    var result = await mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(command);

    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { Error = result.Error });
});

app.Logger.LogInformation("OrderSystem.Api started successfully");
app.Logger.LogInformation("Swagger UI: http://localhost:{Port}/swagger",
    app.Urls.FirstOrDefault()?.Split(':').LastOrDefault() ?? "5000");

app.Run();

// Make Program class available for AppHost reference
public partial class Program { }
