using Catga;
using Catga.DependencyInjection;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ===== Catga Configuration =====
builder.Services
    .AddCatga()
    .UseMemoryPack()
    .WithTracing()
    .ForDevelopment();

builder.Services.AddInMemoryTransport();
builder.Services.AddInMemoryPersistence();

// ===== Business Services =====
builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
builder.Services.AddSingleton<IInventoryService, DistributedInventoryService>();
builder.Services.AddSingleton<IPaymentService, SimulatedPaymentService>();

// ===== Auto-register handlers (source generated) =====
builder.Services.AddGeneratedHandlers();

// ===== Swagger =====
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Auto-generated endpoints from [Route] attributes
Catga.Generated.CatgaEndpointExtensions.MapCatgaEndpoints(app);

app.Run();

namespace OrderSystem.Api
{
    /// <summary>Marker class for WebApplicationFactory in tests.</summary>
    public partial class Program;
}
