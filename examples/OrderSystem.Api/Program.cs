using Catga;
using Catga.AspNetCore;
using Catga.DependencyInjection;
using Catga.Abstractions;
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

// Register distributed services (rate limiter and leader election are optional)
builder.Services.AddSingleton<IInventoryService, OrderSystem.Api.Services.DistributedInventoryService>();
builder.Services.AddSingleton<IPaymentService, OrderSystem.Api.Services.SimulatedPaymentService>();

// Register leader election background service
builder.Services.AddHostedService<OrderSystem.Api.Handlers.LeaderElectionBackgroundService>();

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
