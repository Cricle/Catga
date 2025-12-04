using Catga;
using Catga.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Handlers;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Catga setup - one line
builder.Services.AddCatga().UseMemoryPack().WithTracing().ForDevelopment();
builder.Services.AddInMemoryTransport();
builder.Services.AddInMemoryPersistence();

// Business services only
builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
builder.Services.AddSingleton<IInventoryService, DistributedInventoryService>();
builder.Services.AddSingleton<IPaymentService, SimulatedPaymentService>();

// Auto-register handlers (source generated)
builder.Services.AddGeneratedHandlers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Endpoints
app.MapCatgaRequest<CreateOrderFlowCommand, OrderCreatedResult>("/api/orders");
app.MapCatgaQuery<GetOrderQuery, Order?>("/api/orders/{orderId}");
app.MapCatgaQuery<GetUserOrdersQuery, List<Order>>("/api/users/{customerId}/orders");
app.MapCatgaRequest<ProcessOutboxCommand>("/api/outbox/process");

app.Run();

namespace OrderSystem.Api
{
    /// <summary>Marker class for WebApplicationFactory in tests.</summary>
    public partial class Program { }
}
