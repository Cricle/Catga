using Catga;
using Catga.DependencyInjection;
using Catga.Distributed;
using Microsoft.EntityFrameworkCore;
using OrderSystem;

var builder = WebApplication.CreateBuilder(args);

// Get deployment mode from environment variable
var deploymentMode = builder.Configuration.GetValue<string>("DeploymentMode") ?? "Standalone";
var nodeId = builder.Configuration.GetValue<string>("NodeId") ?? $"node-{Environment.MachineName}";

Console.WriteLine($"🚀 Starting OrderSystem in {deploymentMode} mode, NodeId: {nodeId}");

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
        builder.Services.AddCatga()
            .AddInMemoryMediator()
            .AddHandlers(typeof(Program).Assembly);
        break;

    case "distributed-redis":
        // Distributed mode with Redis transport
        Console.WriteLine("🔴 Configuring Distributed mode (Redis)");
        var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
        builder.Services.AddCatga()
            .AddInMemoryMediator()
            .AddHandlers(typeof(Program).Assembly)
            .AddRedisDistributedLock(redisConnection)
            .AddRedisDistributedCache(redisConnection)
            .AddRedisCluster(options =>
            {
                options.NodeId = nodeId;
                options.ConnectionString = redisConnection;
                options.HeartbeatInterval = TimeSpan.FromSeconds(5);
                options.NodeTimeout = TimeSpan.FromSeconds(15);
            });
        break;

    case "distributed-nats":
    case "cluster":
        // Distributed mode with NATS transport and cluster
        Console.WriteLine("🟢 Configuring Distributed/Cluster mode (NATS)");
        var natsUrl = builder.Configuration.GetValue<string>("Nats:Url") ?? "nats://localhost:4222";
        builder.Services.AddCatga()
            .AddInMemoryMediator()
            .AddHandlers(typeof(Program).Assembly)
            .AddNatsCluster(options =>
            {
                options.NodeId = nodeId;
                options.NatsUrl = natsUrl;
                options.StreamName = "catga-orders";
                options.BucketName = "catga-orders-kv";
                options.HeartbeatInterval = TimeSpan.FromSeconds(5);
                options.NodeTimeout = TimeSpan.FromSeconds(15);
            });
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

// ==================== Order API Endpoints ====================

app.MapPost("/api/orders", async (CreateOrderCommand command, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync(command);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
})
.WithName("CreateOrder")
.WithOpenApi();

app.MapPost("/api/orders/{orderId:long}/process", async (long orderId, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync(new ProcessOrderCommand { OrderId = orderId });
    return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
})
.WithName("ProcessOrder")
.WithOpenApi();

app.MapPost("/api/orders/{orderId:long}/complete", async (long orderId, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync(new CompleteOrderCommand { OrderId = orderId });
    return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
})
.WithName("CompleteOrder")
.WithOpenApi();

app.MapPost("/api/orders/{orderId:long}/cancel", async (long orderId, string reason, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync(new CancelOrderCommand { OrderId = orderId, Reason = reason });
    return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
})
.WithName("CancelOrder")
.WithOpenApi();

app.MapGet("/api/orders/{orderId:long}", async (long orderId, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync(new GetOrderQuery { OrderId = orderId });
    return result.IsSuccess && result.Value != null ? Results.Ok(result.Value) : Results.NotFound();
})
.WithName("GetOrder")
.WithOpenApi();

app.MapGet("/api/orders/customer/{customerName}", async (string customerName, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync(new GetOrdersByCustomerQuery { CustomerName = customerName });
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
})
.WithName("GetOrdersByCustomer")
.WithOpenApi();

app.MapGet("/api/orders/pending", async (ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync(new GetPendingOrdersQuery());
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
})
.WithName("GetPendingOrders")
.WithOpenApi();

// Health check endpoint
app.MapGet("/health", () => new
{
    Status = "Healthy",
    DeploymentMode = deploymentMode,
    NodeId = nodeId,
    Timestamp = DateTime.UtcNow
})
.WithName("HealthCheck")
.WithOpenApi();

Console.WriteLine($"✅ OrderSystem started in {deploymentMode} mode");
Console.WriteLine($"📍 NodeId: {nodeId}");
Console.WriteLine($"🌐 Listening on: {string.Join(", ", builder.WebHost.GetSetting("urls")?.Split(';') ?? new[] { "http://localhost:5000" })}");

app.Run();

