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
// Use Minimal() for production (disables logging/tracing for max performance)
// Use ForDevelopment() for development (enables detailed logging)
var isDevelopment = builder.Environment.IsDevelopment();
builder.Services
    .AddCatga(options =>
    {
        if (isDevelopment)
            options.ForDevelopment();  // Detailed logging for debugging
        else
            options.Minimal();         // Max performance for production
    })
    .UseMemoryPack();                  // High-performance binary serialization

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
// Note: Use AddSingleton for stateless behaviors (better performance)
// ============================================
builder.Services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
builder.Services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

// ============================================
// Command/Query Handlers
// Note: Use AddSingleton for stateless handlers (better performance)
// ============================================
builder.Services.AddSingleton<IRequestHandler<CreateOrderCommand, OrderCreatedResult>, CreateOrderHandler>();
builder.Services.AddSingleton<IRequestHandler<CreateOrderFlowCommand, OrderCreatedResult>, CreateOrderFlowHandler>();
builder.Services.AddSingleton<IRequestHandler<CancelOrderCommand>, CancelOrderHandler>();
builder.Services.AddSingleton<IRequestHandler<GetOrderQuery, Order?>, GetOrderHandler>();
builder.Services.AddSingleton<IRequestHandler<GetUserOrdersQuery, List<Order>>, GetUserOrdersHandler>();

// ============================================
// Event Handlers (Multiple handlers per event)
// Note: Use AddSingleton for stateless handlers (better performance)
// ============================================
builder.Services.AddSingleton<IEventHandler<OrderCreatedEvent>, OrderCreatedEventHandler>();
builder.Services.AddSingleton<IEventHandler<OrderCreatedEvent>, SendOrderNotificationHandler>();
builder.Services.AddSingleton<IEventHandler<OrderCancelledEvent>, OrderCancelledEventHandler>();
builder.Services.AddSingleton<IEventHandler<OrderConfirmedEvent>, OrderConfirmedEventHandler>();

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
