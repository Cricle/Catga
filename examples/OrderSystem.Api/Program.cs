using Catga;
using Catga.AspNetCore;
using Catga.AspNetCore.Extensions;
using Catga.Debugger.AspNetCore.DependencyInjection;
using Catga.Debugger.DependencyInjection;
using Catga.DependencyInjection;
using Catga.Handlers;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Handlers;
using OrderSystem.Api.Services;
using OrderSystem.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddCatga().UseMemoryPack().WithDebug().ForDevelopment();
builder.Services.AddInMemoryTransport();

if (builder.Environment.IsDevelopment())
    builder.Services.AddCatgaDebuggerWithAspNetCore();

// Register handlers and services
builder.Services.AddOrderSystemHandlers();
builder.Services.AddOrderSystemServices();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add CORS for SignalR in development
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
    });
}

var app = builder.Build();

app.MapDefaultEndpoints();

// Use CorrelationId middleware (must be before routing)
app.UseCorrelationId();

// Use CORS before endpoints
if (app.Environment.IsDevelopment())
{
    app.UseCors();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapCatgaDebugger("/debug");
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapCatgaRequest<CreateOrderCommand, OrderCreatedResult>("/api/orders")
    .WithName("CreateOrder").WithTags("Orders");

app.MapPost("/api/orders/cancel", async (CancelOrderCommand cmd, ICatgaMediator m) =>
{
    var result = await m.SendAsync(cmd);
    return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
}).WithName("CancelOrder").WithTags("Orders");

app.MapCatgaQuery<GetOrderQuery, Order?>("/api/orders/{orderId}")
    .WithName("GetOrder").WithTags("Orders");

app.MapPost("/demo/order-success", async (ICatgaMediator m) =>
{
    var items = new List<OrderItem>
    {
        new() { ProductId = "PROD-001", ProductName = "iPhone 15", Quantity = 1, UnitPrice = 5999m },
        new() { ProductId = "PROD-002", ProductName = "AirPods Pro", Quantity = 2, UnitPrice = 1999m }
    };
    var result = await m.SendAsync<CreateOrderCommand, OrderCreatedResult>(
        new("DEMO-CUST-001", items, "123 Success Street, Beijing", "Alipay"));

    return Results.Ok(new
    {
        result.IsSuccess,
        OrderId = result.Value?.OrderId,
        TotalAmount = result.Value?.TotalAmount,
        Message = result.IsSuccess ? "✅ Order created successfully!" : result.Error,
        Metadata = result.Metadata?.GetAll()
    });
}).WithName("DemoOrderSuccess").WithTags("Demo");

app.MapPost("/demo/order-failure", async (ICatgaMediator m) =>
{
    var items = new List<OrderItem>
    {
        new() { ProductId = "PROD-003", ProductName = "MacBook Pro", Quantity = 1, UnitPrice = 16999m },
        new() { ProductId = "PROD-004", ProductName = "Magic Mouse", Quantity = 1, UnitPrice = 649m }
    };
    var result = await m.SendAsync<CreateOrderCommand, OrderCreatedResult>(
        new("DEMO-CUST-002", items, "456 Failure Road, Shanghai", "FAIL-CreditCard"));

    return Results.Ok(new
    {
        result.IsSuccess,
        result.Error,
        Message = result.IsSuccess ? "Order created" : "❌ Order creation failed! Automatic rollback completed.",
        RollbackDetails = result.Metadata?.GetAll(),
        Explanation = "Payment validation failed, triggering automatic rollback"
    });
}).WithName("DemoOrderFailure").WithTags("Demo");

app.MapGet("/demo/compare", () => Results.Ok(new
{
    Title = "Order Creation Flow Comparison",
    SuccessFlow = new
    {
        Endpoint = "POST /demo/order-success",
        Steps = new[] { "1. ✅ Check stock", "2. ✅ Save order", "3. ✅ Reserve inventory",
                        "4. ✅ Validate payment", "5. ✅ Publish event" }
    },
    FailureFlow = new
    {
        Endpoint = "POST /demo/order-failure",
        Steps = new[] { "1. ✅ Check stock", "2. ✅ Save order", "3. ✅ Reserve inventory",
                        "4. ❌ Validate payment (FAILED)", "5. 🔄 Rollback: Release inventory",
                        "6. 🔄 Rollback: Delete order" }
    },
    Features = new[] { "✨ Automatic error handling", "✨ Custom rollback logic",
                       "✨ Rich metadata", "✨ Event-driven architecture" }
})).WithName("DemoComparison").WithTags("Demo");

app.Logger.LogInformation("🚀 OrderSystem started | UI: http://localhost:5000 | Swagger: /swagger | Debug: /debug");

app.Run();

public partial class Program { }
