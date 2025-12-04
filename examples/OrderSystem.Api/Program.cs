using Catga;
using Catga.DependencyInjection;
using Catga.Generated;
using OrderSystem.Api.Services;

// Enable auto endpoint generation
[assembly: CatgaEndpoints(RoutePrefix = "/api")]

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

// Auto-map all handlers to endpoints (source generated)
app.MapCatgaEndpoints();

app.Run();
