using Catga;
using Catga.AspNetCore;
using Catga.DependencyInjection;
using Catga.Abstractions;
using Catga.Flow;
using Catga.Observability;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Handlers;
using OrderSystem.Api.Infrastructure;
using OrderSystem.Api.Infrastructure.Caching;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using System.Diagnostics.Metrics;
using System.Reflection;

// Configure Serilog for structured logging
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}",
        theme: AnsiConsoleTheme.Code)
    .CreateBootstrapLogger();

try
{
    Log.Information("🚀 Starting OrderSystem API...");

    var builder = WebApplication.CreateBuilder(args);

    // Configure Serilog
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    builder.AddServiceDefaults();

    // Add configuration
    builder.Configuration
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables();

// ============================================================
// Catga Full Feature Configuration
// ============================================================
// Distributed/Cluster: Set WorkerId via args or environment
// Single node: dotnet run
// Multi-node: dotnet run -- 1 (node1), dotnet run -- 2 (node2)
// Production: Set CATGA_WORKER_ID environment variable
// ============================================================

var catgaBuilder = builder.Services
    .AddCatga(o => o.EndpointNamingConvention = Catga.Generated.EndpointNaming.GetConvention())
    .WithTracing()                    // OpenTelemetry distributed tracing
    .WithLogging()                    // Structured logging
    .UseMemoryPack()                  // High-performance serialization
    .UseResilience()                  // Polly retry/circuit breaker
    .UseInbox()                       // Exactly-once delivery
    .UseOutbox()                      // Reliable event publishing
    .UseDeadLetterQueue()             // Failed message handling
    .UseAutoCompensation();           // Automatic compensation on failure

if (args.Length > 0 && int.TryParse(args[0], out var workerId))
{
    catgaBuilder.UseWorkerId(workerId);
    builder.WebHost.UseUrls($"http://localhost:{5000 + workerId}");
    Console.WriteLine($"[OrderSystem] 🌐 Using WorkerId from args: {workerId}, Port: {5000 + workerId}");
}
else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CATGA_WORKER_ID")))
{
    catgaBuilder.UseWorkerIdFromEnvironment();
    Console.WriteLine("[OrderSystem] 🌐 Using WorkerId from environment variable");
}
else
{
    Console.WriteLine("[OrderSystem] ⚙️ Single-node development mode (random WorkerId)");
}

catgaBuilder.ForDevelopment();

// Transport and Persistence
builder.Services.AddInMemoryTransport();
builder.Services.AddInMemoryPersistence();

// Register compensation publisher for automatic rollback
builder.Services.AddSingleton<Catga.Pipeline.Behaviors.ICompensationPublisher<OrderSystem.Api.Messages.CreateOrderCommand>,
    OrderSystem.Api.Services.CreateOrderCompensation>();

// Register distributed services
builder.Services.AddSingleton<IInventoryService, OrderSystem.Api.Services.DistributedInventoryService>();
builder.Services.AddSingleton<IPaymentService, OrderSystem.Api.Services.SimulatedPaymentService>();

// Configure OpenTelemetry
var serviceName = "OrderSystem.Api";
var serviceVersion = "1.0.0";

builder.Services.AddOpenTelemetry()
    .WithTracing(tracerProviderBuilder =>
        tracerProviderBuilder
            .AddSource(serviceName)
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: serviceVersion))
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter())
    .WithMetrics(metricsProviderBuilder =>
        metricsProviderBuilder
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: serviceVersion))
            .AddAspNetCoreInstrumentation()
            .AddRuntimeInstrumentation()
            .AddOtlpExporter());

// Add health checks
builder.Services.AddHealthChecks();

// Register application services
builder.Services.AddGeneratedHandlers();
builder.Services.AddGeneratedServices();

// Add OrderSystem services with configuration
builder.Services.AddOrderSystemServices(builder.Configuration);
builder.Services.AddOrderSystemHandlers();

// Add request logging
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.All;
    options.RequestBodyLogLimit = 4096;
    options.ResponseBodyLogLimit = 4096;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
        diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
        diagnosticContext.Set("RemoteIpAddress", httpContext.Connection.RemoteIpAddress?.ToString());
    };
});

app.UseHttpLogging();
app.MapDefaultEndpoints();

// Add health check endpoint
app.MapHealthChecks("/health", new()
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration
            })
        });
        await context.Response.WriteAsync(result);
    }
}).AllowAnonymous();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapCatgaRequest<CreateOrderCommand, OrderCreatedResult>("/api/orders")
    .WithName("CreateOrder").WithTags("Orders");

// Flow orchestration version - automatic compensation on failure
app.MapCatgaRequest<CreateOrderFlowCommand, OrderCreatedResult>("/api/orders/flow")
    .WithName("CreateOrderFlow").WithTags("Orders", "Flow");

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
        ErrorCode = result.ErrorCode
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
        result.ErrorCode,
        Message = result.IsSuccess ? "Order created" : "❌ Order creation failed! Automatic rollback completed.",
        Explanation = "Payment validation failed, triggering automatic rollback"
    });
}).WithName("DemoOrderFailure").WithTags("Demo");

// ============================================================
// Flow Orchestration Demo - Real Handler with Automatic Compensation
// ============================================================

app.MapPost("/demo/flow/order-success", async (ICatgaMediator m) =>
{
    var items = new List<OrderItem>
    {
        new() { ProductId = "FLOW-001", ProductName = "Surface Pro 9", Quantity = 1, UnitPrice = 8999m },
        new() { ProductId = "FLOW-002", ProductName = "Surface Pen", Quantity = 2, UnitPrice = 799m }
    };

    var result = await m.SendAsync<CreateOrderFlowCommand, OrderCreatedResult>(
        new("FLOW-CUST-001", items, "Flow Success Street, Beijing", "Alipay"));

    return Results.Ok(new
    {
        result.IsSuccess,
        OrderId = result.Value?.OrderId,
        TotalAmount = result.Value?.TotalAmount,
        Message = result.IsSuccess
            ? "✅ Flow orchestration completed successfully - no compensation needed"
            : $"❌ Flow failed: {result.Error}",
        FlowSteps = new[]
        {
            "1. ✅ CheckInventory - Stock available",
            "2. ✅ CreateOrder - Order saved (compensation registered)",
            "3. ✅ ReserveInventory - Stock reserved (compensation registered)",
            "4. ✅ ProcessPayment - Payment processed (compensation registered)",
            "5. ✅ ConfirmOrder - Order confirmed"
        }
    });
}).WithName("DemoFlowOrderSuccess").WithTags("Flow Demo");

app.MapPost("/demo/flow/order-failure", async (ICatgaMediator m) =>
{
    var items = new List<OrderItem>
    {
        new() { ProductId = "FLOW-003", ProductName = "Xbox Series X", Quantity = 1, UnitPrice = 3999m },
        new() { ProductId = "FLOW-004", ProductName = "Xbox Controller", Quantity = 2, UnitPrice = 499m }
    };

    // Use FAIL- prefix to trigger payment failure
    var result = await m.SendAsync<CreateOrderFlowCommand, OrderCreatedResult>(
        new("FLOW-CUST-002", items, "Flow Failure Road, Shanghai", "FAIL-CreditCard"));

    return Results.Ok(new
    {
        result.IsSuccess,
        result.Error,
        Message = "❌ Flow failed at payment step - automatic compensation executed in reverse order",
        FlowSteps = new[]
        {
            "1. ✅ CheckInventory - Stock available",
            "2. ✅ CreateOrder - Order saved (compensation registered)",
            "3. ✅ ReserveInventory - Stock reserved (compensation registered)",
            "4. ❌ ProcessPayment - FAILED (payment declined)",
            "--- AUTOMATIC COMPENSATION (reverse order) ---",
            "🔄 ReleaseInventory - Stock released",
            "🔄 DeleteOrder - Order marked as failed"
        },
        Explanation = "Payment failed → Flow automatically executed compensations in reverse order"
    });
}).WithName("DemoFlowOrderFailure").WithTags("Flow Demo");

app.MapGet("/demo/compare", () => Results.Ok(new
{
    Title = "Traditional vs Flow Orchestration Comparison",
    Traditional = new
    {
        Endpoints = new[] { "POST /demo/order-success", "POST /demo/order-failure" },
        Description = "Manual try-catch with explicit HandleOrderFailure calls",
        Pros = new[] { "Full control", "Explicit error handling" },
        Cons = new[] { "Verbose code", "Easy to miss rollback", "Hard to maintain" }
    },
    FlowOrchestration = new
    {
        Endpoints = new[] { "POST /demo/flow/order-success", "POST /demo/flow/order-failure" },
        Description = "Automatic compensation in reverse order on any failure",
        Pros = new[] { "Zero boilerplate", "Automatic rollback", "Declarative", "AOT compatible" },
        Cons = new[] { "Learning curve" }
    },
    Recommendation = "Use Flow for multi-step operations requiring rollback on failure"
})).WithName("DemoComparison").WithTags("Demo");

// ============================================================
// Flow Info
// ============================================================

app.MapGet("/demo/flow-info", () => Results.Ok(new
{
    Title = "Flow Orchestration - Zero-Cost Automatic Compensation",
    Description = "Catga Flow provides saga-like orchestration with automatic compensation on failure",
    Endpoints = new[]
    {
        new { Method = "POST", Path = "/demo/flow/order-success", Description = "All steps succeed - no compensation" },
        new { Method = "POST", Path = "/demo/flow/order-failure", Description = "Payment fails - auto compensation" }
    },
    Features = new[]
    {
        "✨ AsyncLocal context - implicit propagation, no manual passing",
        "✨ Automatic reverse-order compensation on failure",
        "✨ Zero reflection, AOT compatible",
        "✨ Integrates with existing Pipeline (retry, timeout, outbox)"
    },
    CodeExample = @"
// Use Flow in Handler
await using var flow = mediator.BeginFlow(""CreateOrder"");

// Step 1: Create order
await orderRepository.SaveAsync(order, ct);
flow.RegisterCompensation(async ct => {
    order.Status = OrderStatus.Failed;
    await orderRepository.UpdateAsync(order, ct);
}, ""DeleteOrder"");

// Step 2: Reserve inventory
await inventoryService.ReserveStockAsync(orderId, items, ct);
flow.RegisterCompensation(async ct => {
    await inventoryService.ReleaseStockAsync(orderId, items, ct);
}, ""ReleaseInventory"");

// Step 3: Process payment
await paymentService.ProcessPaymentAsync(orderId, amount, method, ct);

flow.Commit(); // Success - no compensation
// If any step fails before Commit, DisposeAsync auto-compensates in reverse order
"
})).WithName("DemoFlowInfo").WithTags("Flow Demo");

// ============================================================
// Batch Processing Demo
// ============================================================

app.MapPost("/demo/batch", async (ICatgaMediator m) =>
{
    var queries = Enumerable.Range(1, 5)
        .Select(i => new GetOrderQuery($"BATCH-ORDER-{i:D3}"))
        .ToList();

    var results = await m.SendBatchAsync<GetOrderQuery, Order?>(queries);

    return Results.Ok(new
    {
        Title = "Batch Processing Demo",
        BatchSize = queries.Count,
        Results = results.Select((r, i) => new
        {
            Query = queries[i].OrderId,
            Found = r.IsSuccess && r.Value != null,
            Error = r.Error
        }),
        Message = "Batch processing executes multiple requests efficiently"
    });
}).WithName("DemoBatch").WithTags("Advanced Demo");

// ============================================================
// Event Publishing Demo
// ============================================================

app.MapPost("/demo/events", async (ICatgaMediator m) =>
{
    var orderId = $"EVT-{DateTime.UtcNow:yyyyMMddHHmmss}";

    // Publish event - all handlers execute in parallel
    await m.PublishAsync(new OrderCreatedEvent(
        orderId,
        "DEMO-CUST",
        new List<OrderItem> { new() { ProductId = "P1", ProductName = "Demo", Quantity = 1, UnitPrice = 99m } },
        99m,
        DateTime.UtcNow));

    return Results.Ok(new
    {
        Title = "Event Publishing Demo",
        EventType = nameof(OrderCreatedEvent),
        OrderId = orderId,
        Message = "Event published - multiple handlers executed in parallel",
        Handlers = new[]
        {
            "OrderCreatedNotificationHandler - sends notifications",
            "OrderCreatedAnalyticsHandler - updates analytics",
            "OrderCreatedInventoryHandler - updates inventory",
            "OrderCreatedAuditHandler - creates audit log"
        }
    });
}).WithName("DemoEvents").WithTags("Advanced Demo");

string firstUrl = "http://localhost:5000";
foreach (var u in app.Urls) { firstUrl = u; break; }
app.Logger.LogInformation($"🚀 OrderSystem started | UI: {firstUrl} | Swagger: /swagger | Jaeger: http://localhost:16686");

app.Run();

}
catch (Exception ex)
{
    Log.Fatal(ex, "Application start-up failed");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
