using Catga;
using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.Pipeline;
using OrderSystem.Api.Behaviors;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Handlers;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ============================================
// Catga Configuration (Best Practices)
// ============================================
builder.Services
    .AddCatga()
    .UseMemoryPack()           // High-performance binary serialization
    .ForDevelopment();         // Development mode with detailed logging

// Transport configuration (env: CATGA_TRANSPORT = InMemory | Redis | NATS)
var transport = Environment.GetEnvironmentVariable("CATGA_TRANSPORT") ?? "InMemory";
switch (transport.ToLower())
{
    case "redis":
        var redisConn = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379";
        builder.Services.AddRedisTransport(redisConn);
        break;
    case "nats":
        var natsUrl = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";
        builder.Services.AddNatsTransport(natsUrl);
        break;
    default:
        builder.Services.AddInMemoryTransport();
        break;
}

// Persistence configuration (env: CATGA_PERSISTENCE = InMemory | Redis)
var persistence = Environment.GetEnvironmentVariable("CATGA_PERSISTENCE") ?? "InMemory";
switch (persistence.ToLower())
{
    case "redis":
        var redisConnPersist = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379";
        builder.Services.AddRedisPersistence(redisConnPersist);
        break;
    default:
        builder.Services.AddInMemoryPersistence();
        break;
}

// ============================================
// Services
// ============================================
builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();

// ============================================
// Pipeline Behaviors (Cross-cutting concerns)
// ============================================
builder.Services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
builder.Services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

// ============================================
// Command/Query Handlers
// ============================================
builder.Services.AddScoped<IRequestHandler<CreateOrderCommand, OrderCreatedResult>, CreateOrderHandler>();
builder.Services.AddScoped<IRequestHandler<CreateOrderFlowCommand, OrderCreatedResult>, CreateOrderFlowHandler>();
builder.Services.AddScoped<IRequestHandler<CancelOrderCommand>, CancelOrderHandler>();
builder.Services.AddScoped<IRequestHandler<GetOrderQuery, Order?>, GetOrderHandler>();
builder.Services.AddScoped<IRequestHandler<GetUserOrdersQuery, List<Order>>, GetUserOrdersHandler>();

// ============================================
// Event Handlers (Multiple handlers per event)
// ============================================
builder.Services.AddScoped<IEventHandler<OrderCreatedEvent>, OrderCreatedEventHandler>();
builder.Services.AddScoped<IEventHandler<OrderCreatedEvent>, SendOrderNotificationHandler>();
builder.Services.AddScoped<IEventHandler<OrderCancelledEvent>, OrderCancelledEventHandler>();
builder.Services.AddScoped<IEventHandler<OrderConfirmedEvent>, OrderConfirmedEventHandler>();

// ============================================
// Swagger & Health Checks
// ============================================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapHealthChecks("/health");

// Endpoints
app.MapPost("/api/orders", async (CreateOrderCommand cmd, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(cmd);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
});

// Flow endpoint - demonstrates automatic compensation on failure
app.MapPost("/api/orders/flow", async (CreateOrderFlowCommand cmd, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<CreateOrderFlowCommand, OrderCreatedResult>(cmd);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
}).WithDescription("Create order using Flow pattern with automatic compensation");

app.MapGet("/api/orders/{orderId}", async (string orderId, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<GetOrderQuery, Order?>(new(orderId));
    return result.Value is null ? Results.NotFound() : Results.Ok(result.Value);
});

app.MapPost("/api/orders/{orderId}/cancel", async (string orderId, CancelOrderCommand? body, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync(new CancelOrderCommand(orderId, body?.Reason));
    return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
});

app.MapGet("/api/users/{customerId}/orders", async (string customerId, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<GetUserOrdersQuery, List<Order>>(new(customerId));
    return Results.Ok(result.Value);
});

app.Run();

namespace OrderSystem.Api
{
    public partial class Program;
}
