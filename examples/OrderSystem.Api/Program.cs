using Catga;
using Catga.AspNetCore;
using Catga.Debugger.AspNetCore.DependencyInjection;
using Catga.Debugger.DependencyInjection;
using Catga.DependencyInjection;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Handlers;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ===== Aspire Service Defaults =====
builder.AddServiceDefaults();  // OpenTelemetry, Health Checks, Service Discovery

// ===== Catga Configuration =====
builder.Services.AddCatga()                      // Add Catga core services
    .UseMemoryPack()                             // Serializer (AOT-friendly)
    .WithDebug()                                 // Enable debugging (auto-detects environment)
    .ForDevelopment();                           // Development environment

builder.Services.AddInMemoryTransport();         // Transport layer (replaceable with NATS)

// Graceful lifecycle (using CatgaBuilder)
builder.Services.AddCatgaBuilder(b => b.UseGracefulLifecycle());

// ===== Catga Debugger UI (Optional - Vue 3 + SignalR) =====
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCatgaDebuggerWithAspNetCore();
}

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

    // Map Catga Debugger UI (if enabled)
    app.MapCatgaDebugger("/debug");
    // UI: http://localhost:5000/debug (Vue 3 UI with time-travel)
    // API: http://localhost:5000/debug-api/* (REST endpoints)
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

// Health check
app.MapGet("/health", () => Results.Ok(new
{
    Status = "Healthy",
    Timestamp = DateTime.UtcNow,
    Service = "OrderSystem.Api"
}));

// ===== Demo Endpoints: Success & Failure with Rollback =====

// Demo 1: Successful order creation
app.MapPost("/demo/order-success", async (ICatgaMediator mediator) =>
{
    var command = new CreateOrderCommand(
        CustomerId: "DEMO-CUST-001",
        Items: new List<OrderItem>
        {
            new() { ProductId = "PROD-001", ProductName = "iPhone 15", Quantity = 1, UnitPrice = 5999m },
            new() { ProductId = "PROD-002", ProductName = "AirPods Pro", Quantity = 2, UnitPrice = 1999m }
        },
        ShippingAddress: "123 Success Street, Beijing",
        PaymentMethod: "Alipay"  // Valid payment method
    );

    var result = await mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(command);

    return Results.Ok(new
    {
        Success = result.IsSuccess,
        OrderId = result.Value?.OrderId,
        TotalAmount = result.Value?.TotalAmount,
        Message = result.IsSuccess
            ? "✅ Order created successfully! All steps completed: Stock checked → Order saved → Inventory reserved → Event published"
            : result.Error,
        Metadata = result.Metadata?.GetAll()
    });
}).WithName("DemoOrderSuccess")
  .WithTags("Demo")
  .WithSummary("Demo: Successful order with all steps");

// Demo 2: Failed order with automatic rollback
app.MapPost("/demo/order-failure", async (ICatgaMediator mediator) =>
{
    var command = new CreateOrderCommand(
        CustomerId: "DEMO-CUST-002",
        Items: new List<OrderItem>
        {
            new() { ProductId = "PROD-003", ProductName = "MacBook Pro", Quantity = 1, UnitPrice = 16999m },
            new() { ProductId = "PROD-004", ProductName = "Magic Mouse", Quantity = 1, UnitPrice = 649m }
        },
        ShippingAddress: "456 Failure Road, Shanghai",
        PaymentMethod: "FAIL-CreditCard"  // Will trigger failure
    );

    var result = await mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(command);

    return Results.Ok(new
    {
        Success = result.IsSuccess,
        Error = result.Error,
        Message = result.IsSuccess
            ? "Order created"
            : "❌ Order creation failed! Automatic rollback completed: Inventory released → Order deleted → Failure event published",
        RollbackDetails = result.Metadata?.GetAll(),
        Explanation = "Payment validation failed, triggering automatic rollback of all completed steps"
    });
}).WithName("DemoOrderFailure")
  .WithTags("Demo")
  .WithSummary("Demo: Failed order with automatic rollback");

// Demo 3: Quick comparison endpoint
app.MapGet("/demo/compare", () => Results.Ok(new
{
    Title = "Order Creation Flow Comparison",
    SuccessFlow = new
    {
        Endpoint = "POST /demo/order-success",
        PaymentMethod = "Alipay",
        Steps = new[]
        {
            "1. ✅ Check stock availability",
            "2. ✅ Calculate total amount",
            "3. ✅ Save order to database",
            "4. ✅ Reserve inventory",
            "5. ✅ Validate payment method",
            "6. ✅ Publish OrderCreatedEvent",
            "Result: Order created successfully"
        }
    },
    FailureFlow = new
    {
        Endpoint = "POST /demo/order-failure",
        PaymentMethod = "FAIL-CreditCard",
        Steps = new[]
        {
            "1. ✅ Check stock availability",
            "2. ✅ Calculate total amount",
            "3. ✅ Save order to database",
            "4. ✅ Reserve inventory",
            "5. ❌ Validate payment method (FAILED)",
            "6. 🔄 Rollback: Release inventory",
            "7. 🔄 Rollback: Delete order",
            "8. 📢 Publish OrderFailedEvent",
            "Result: All changes rolled back"
        }
    },
    Features = new[]
    {
        "✨ Automatic error handling via SafeRequestHandler",
        "✨ Custom OnBusinessErrorAsync for rollback logic",
        "✨ Rich metadata in error responses",
        "✨ Event-driven architecture",
        "✨ Zero manual try-catch needed"
    }
})).WithName("DemoComparison")
  .WithTags("Demo")
  .WithSummary("Compare success vs failure flows");

app.Logger.LogInformation("OrderSystem.Api started successfully");
app.Logger.LogInformation("Swagger UI: http://localhost:{Port}/swagger",
    app.Urls.FirstOrDefault()?.Split(':').LastOrDefault() ?? "5000");

app.Run();

// Make Program class available for AppHost reference
public partial class Program { }
