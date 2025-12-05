using Catga;
using Catga.DependencyInjection;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ===== Cluster Mode Detection =====
var clusterEnabled = Environment.GetEnvironmentVariable("Catga__ClusterEnabled") == "true";

// ===== Catga Configuration =====
builder.Services
    .AddCatga()
    .UseMemoryPack()
    .WithTracing()
    .ForDevelopment();

builder.Services.AddInMemoryTransport();
builder.Services.AddInMemoryPersistence();

// ===== Business Services =====
// In cluster mode, use Redis for distributed storage
// In single mode, use in-memory for simplicity
if (clusterEnabled)
{
    // Redis is provided by Aspire via AddRedis() in AppHost
    // Use StackExchange.Redis client with Aspire connection string
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = builder.Configuration.GetConnectionString("redis");
    });
    builder.Services.AddSingleton<IOrderRepository, RedisOrderRepository>();
}
else
{
    builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
}
builder.Services.AddSingleton<IInventoryService, DistributedInventoryService>();
builder.Services.AddSingleton<IPaymentService, SimulatedPaymentService>();

// ===== Auto-register handlers (source generated) =====
builder.Services.AddGeneratedHandlers();

// ===== Health Checks =====
builder.Services.AddHealthChecks();

// ===== Swagger =====
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ===== Static Files (Web UI) =====
app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ===== Health Check Endpoints =====
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

// ===== Cluster Status Endpoints =====
var nodeId = Environment.GetEnvironmentVariable("Catga__NodeId") ?? $"node-{Environment.ProcessId}";

app.MapGet("/api/cluster/status", () => new
{
    ClusterEnabled = clusterEnabled,
    NodeCount = clusterEnabled ? 3 : 1,
    Status = "healthy",
    Timestamp = DateTime.UtcNow
});

app.MapGet("/api/cluster/node", () => new
{
    NodeId = nodeId,
    ProcessId = Environment.ProcessId,
    MachineName = Environment.MachineName,
    StartTime = DateTime.UtcNow,
    ClusterEnabled = clusterEnabled
});

// ===== Auto-generated endpoints from [Route] attributes =====
Catga.Generated.CatgaEndpointExtensions.MapCatgaEndpoints(app);

app.Run();

namespace OrderSystem.Api
{
    /// <summary>Marker class for WebApplicationFactory in tests.</summary>
    public partial class Program;
}
