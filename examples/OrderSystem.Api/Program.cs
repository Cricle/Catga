using Catga;
using Catga.DependencyInjection;
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

// Auto-register handlers
builder.Services.AddGeneratedHandlers();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapHealthChecks("/health");

// Auto-generated endpoints from [Route] attributes
Catga.Generated.CatgaEndpointExtensions.MapCatgaEndpoints(app);

app.Run();

namespace OrderSystem.Api
{
    public partial class Program;
}
