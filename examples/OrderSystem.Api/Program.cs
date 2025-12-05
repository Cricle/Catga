using Catga;
using Catga.Abstractions;
using Catga.DependencyInjection;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Handlers;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Catga
builder.Services
    .AddCatga()
    .UseMemoryPack()
    .ForDevelopment();

builder.Services.AddInMemoryTransport();
builder.Services.AddInMemoryPersistence();

// Services
builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();

// Handlers
builder.Services.AddScoped<IRequestHandler<CreateOrderCommand, OrderCreatedResult>, CreateOrderHandler>();
builder.Services.AddScoped<IRequestHandler<CancelOrderCommand>, CancelOrderHandler>();
builder.Services.AddScoped<IRequestHandler<GetOrderQuery, Order?>, GetOrderHandler>();
builder.Services.AddScoped<IRequestHandler<GetUserOrdersQuery, List<Order>>, GetUserOrdersHandler>();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
