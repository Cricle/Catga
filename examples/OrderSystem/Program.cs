using Catga;
using Catga.DependencyInjection;
using Catga.Distributed;
using Catga.Distributed.Nats.DependencyInjection;
using Catga.Distributed.Redis.DependencyInjection;
using Catga.Persistence.Redis.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using OrderSystem;

var builder = WebApplication.CreateBuilder(args);

// Get deployment mode from environment variable
var deploymentMode = builder.Configuration.GetValue<string>("DeploymentMode") ?? "Standalone";
var nodeId = builder.Configuration.GetValue<string>("NodeId") ?? $"node-{Environment.MachineName}";

Console.WriteLine($"🚀 Starting OrderSystem in {deploymentMode} mode, NodeId: {nodeId}");

// Add Aspire service defaults if in Aspire mode
if (deploymentMode.Equals("Aspire", StringComparison.OrdinalIgnoreCase))
{
    builder.AddServiceDefaults();
}

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add SQLite database
builder.Services.AddDbContext<OrderDbContext>(options =>
{
    var dbPath = Path.Combine(AppContext.BaseDirectory, "orders.db");
    options.UseSqlite($"Data Source={dbPath}");
});

// Configure Catga based on deployment mode
switch (deploymentMode.ToLower())
{
    case "standalone":
        // Standalone mode: In-memory mediator only
        Console.WriteLine("📦 Configuring Standalone mode (In-Memory)");
        builder.Services.AddCatga();
        builder.Services.AddGeneratedHandlers();
        break;

    case "distributed-redis":
        // Distributed mode with Redis transport
        Console.WriteLine("🔴 Configuring Distributed mode (Redis)");
        var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
        var redisEndpoint = builder.Configuration.GetValue<string>("Redis:Endpoint") ?? "http://localhost:5000";
        builder.Services.AddCatga();
        builder.Services.AddGeneratedHandlers();
        builder.Services.AddRedisDistributedLock(redisConnection);
        builder.Services.AddRedisDistributedCache();
        builder.Services.AddRedisCluster(
            redisConnectionString: redisConnection,
            nodeId: nodeId,
            endpoint: redisEndpoint);
        break;

    case "distributed-nats":
    case "cluster":
        // Distributed mode with NATS transport and cluster
        Console.WriteLine("🟢 Configuring Distributed/Cluster mode (NATS)");
        var natsUrl = builder.Configuration.GetValue<string>("Nats:Url") ?? "nats://localhost:4222";
        var natsEndpoint = builder.Configuration.GetValue<string>("Nats:Endpoint") ?? "http://localhost:5000";
        builder.Services.AddCatga();
        builder.Services.AddGeneratedHandlers();
        builder.Services.AddNatsCluster(
            natsUrl: natsUrl,
            nodeId: nodeId,
            endpoint: natsEndpoint);
        break;

    case "aspire":
        // Aspire mode with service discovery and orchestration
        Console.WriteLine("⭐ Configuring Aspire mode (Service Discovery + Redis + NATS)");
        builder.Services.AddCatga();
        builder.Services.AddGeneratedHandlers();
        
        // Redis connection from Aspire service discovery
        var aspireRedisConnection = builder.Configuration.GetConnectionString("redis") ?? "localhost:6379";
        builder.Services.AddRedisDistributedLock(aspireRedisConnection);
        builder.Services.AddRedisDistributedCache();
        
        // NATS connection from Aspire service discovery
        var aspireNatsUrl = builder.Configuration.GetConnectionString("nats") ?? "nats://localhost:4222";
        var aspireEndpoint = builder.Configuration.GetValue<string>("ASPNETCORE_URLS")?.Split(';')[0] ?? "http://localhost:5000";
        builder.Services.AddNatsCluster(
            natsUrl: aspireNatsUrl,
            nodeId: nodeId,
            endpoint: aspireEndpoint);
        break;

    default:
        throw new InvalidOperationException($"Unknown deployment mode: {deploymentMode}");
}

var app = builder.Build();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
    Console.WriteLine("✅ Database initialized");
}

// Configure middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

// ✨ Use Catga ASP.NET Core integration (similar to CAP pattern)
app.UseCatga();

// ==================== Order API Endpoints (Catga Style) ====================

// Command endpoints - direct CQRS mapping
app.MapCatgaRequest<CreateOrderCommand, CreateOrderResult>("/api/orders")
   .WithOpenApi();

app.MapPost("/api/orders/{orderId:long}/process", async (long orderId, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<ProcessOrderCommand, bool>(new ProcessOrderCommand { OrderId = orderId });
    return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
})
.WithName("ProcessOrder")
.WithOpenApi();

app.MapPost("/api/orders/{orderId:long}/complete", async (long orderId, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<CompleteOrderCommand, bool>(new CompleteOrderCommand { OrderId = orderId });
    return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
})
.WithName("CompleteOrder")
.WithOpenApi();

app.MapPost("/api/orders/{orderId:long}/cancel", async (long orderId, string reason, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<CancelOrderCommand, bool>(new CancelOrderCommand { OrderId = orderId, Reason = reason });
    return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
})
.WithName("CancelOrder")
.WithOpenApi();

// Query endpoints - using Catga extensions
app.MapGet("/api/orders/{orderId:long}", async (long orderId, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<GetOrderQuery, OrderDto?>(new GetOrderQuery { OrderId = orderId });
    return result.IsSuccess && result.Value != null ? Results.Ok(result.Value) : Results.NotFound();
})
.WithName("GetOrder")
.WithOpenApi();

app.MapGet("/api/orders/customer/{customerName}", async (string customerName, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<GetOrdersByCustomerQuery, List<OrderDto>>(new GetOrdersByCustomerQuery { CustomerName = customerName });
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
})
.WithName("GetOrdersByCustomer")
.WithOpenApi();

app.MapGet("/api/orders/pending", async (ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<GetPendingOrdersQuery, List<OrderDto>>(new GetPendingOrdersQuery());
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
})
.WithName("GetPendingOrders")
.WithOpenApi();

// Health check endpoint
if (deploymentMode.Equals("Aspire", StringComparison.OrdinalIgnoreCase))
{
    // Use Aspire default endpoints
    app.MapDefaultEndpoints();
}
else
{
    // Custom health check for non-Aspire modes
    app.MapGet("/health", () => new
    {
        Status = "Healthy",
        DeploymentMode = deploymentMode,
        NodeId = nodeId,
        Timestamp = DateTime.UtcNow
    })
    .WithName("HealthCheck")
    .WithOpenApi();
}

Console.WriteLine($"✅ OrderSystem started in {deploymentMode} mode");
Console.WriteLine($"📍 NodeId: {nodeId}");
Console.WriteLine($"🌐 Listening on: {string.Join(", ", builder.WebHost.GetSetting("urls")?.Split(';') ?? new[] { "http://localhost:5000" })}");

app.Run();

