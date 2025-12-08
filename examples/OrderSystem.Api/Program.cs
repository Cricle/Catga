using Catga;
using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.EventSourcing;
using Catga.Persistence.Stores;
using Catga.Resilience;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Extensions;
using OrderSystem.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ============================================
// Catga Configuration
// ============================================
var isDevelopment = builder.Environment.IsDevelopment();
builder.Services
    .AddCatga(options => { if (isDevelopment) options.ForDevelopment(); else options.Minimal(); })
    .UseMemoryPack();

// Transport (env: CATGA_TRANSPORT = InMemory | Redis | NATS)
var transport = Environment.GetEnvironmentVariable("CATGA_TRANSPORT") ?? "InMemory";
switch (transport.ToLower())
{
    case "redis": builder.Services.AddRedisTransport(Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379"); break;
    case "nats": builder.Services.AddNatsTransport(Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222"); break;
    default: builder.Services.AddInMemoryTransport(); break;
}

// Persistence (env: CATGA_PERSISTENCE = InMemory | Redis)
builder.Services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
var persistence = Environment.GetEnvironmentVariable("CATGA_PERSISTENCE") ?? "InMemory";
switch (persistence.ToLower())
{
    case "redis":
        var redisConn = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379";
        builder.Services.AddRedisPersistence(redisConn);
        builder.Services.AddRedisEventStore();
        builder.Services.AddRedisSnapshotStore();
        break;
    default:
        builder.Services.AddInMemoryPersistence();
        builder.Services.AddSingleton<IEventStore, InMemoryEventStore>();
        break;
}

// ============================================
// Application Services
// ============================================
builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
builder.Services.AddOrderSystemHandlers();
builder.Services.AddOrderSystemBehaviors();
builder.Services.AddOrderSystemEventSourcing();
builder.Services.AddTimeTravelService<OrderAggregate>();

// Swagger & Health
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHealthChecks("/health");

// ============================================
// Endpoints
// ============================================
app.MapOrderEndpoints();
app.MapTimeTravelEndpoints();
app.MapProjectionEndpoints();
app.MapSubscriptionEndpoints();
app.MapAuditEndpoints();
app.MapSnapshotEndpoints();

app.Run();

namespace OrderSystem.Api
{
    public partial class Program;
}
