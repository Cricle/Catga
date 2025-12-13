// =============================================================================
// OrderSystem.Api - Catga Best Practices Example
// =============================================================================
// This file demonstrates the recommended patterns for building production-ready
// applications with Catga CQRS framework.
// =============================================================================

using Catga;
using Catga.DependencyInjection;
using Catga.EventSourcing;
using Catga.Flow.Extensions;
using OrderSystem.Api.Configuration;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Endpoints;
using OrderSystem.Api.Infrastructure;
using OrderSystem.Api.Services;
using Serilog;

// =============================================================================
// 1. Bootstrap Logging
// =============================================================================

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Serilog from appsettings
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // ==========================================================================
    // 2. Configuration - Options Pattern
    // ==========================================================================

    builder.Services.Configure<CatgaOptions>(
        builder.Configuration.GetSection(CatgaOptions.SectionName));

    var catgaOptions = builder.Configuration
        .GetSection(CatgaOptions.SectionName)
        .Get<CatgaOptions>() ?? new CatgaOptions();

    // ==========================================================================
    // 3. Catga Framework
    // ==========================================================================

    var catgaBuilder = builder.Services
        .AddCatga(opt =>
        {
            if (catgaOptions.DevelopmentMode || builder.Environment.IsDevelopment())
                opt.ForDevelopment();
            else
                opt.Minimal();
        })
        .UseMemoryPack();

    // Persistence: InMemory (default) | Redis | Nats
    switch (catgaOptions.Persistence.ToLower())
    {
        case "redis":
            catgaBuilder.UseRedis(catgaOptions.RedisConnection);
            break;
        case "nats":
            builder.Services.AddNatsConnection(catgaOptions.NatsUrl);
            catgaBuilder.UseNats();
            break;
        default:
            catgaBuilder.UseInMemory();
            break;
    }

    // Transport: InMemory (default) | Redis | Nats
    _ = catgaOptions.Transport.ToLower() switch
    {
        "redis" => builder.Services.AddRedisTransport(catgaOptions.RedisConnection),
        "nats" => builder.Services.AddNatsTransport(catgaOptions.NatsUrl),
        _ => builder.Services.AddInMemoryTransport()
    };

    // Flow DSL
    builder.Services.AddFlowDsl(options =>
    {
        options.AutoRegisterFlows = true;
        options.EnableMetrics = true;
        options.MaxRetryAttempts = 3;
        options.StepTimeout = TimeSpan.FromMinutes(5);
    });

    // ==========================================================================
    // 4. Application Services
    // ==========================================================================

    builder.Services.AddCatgaHandlers();          // Source generator auto-registration
    builder.Services.AddOrderSystem();             // Domain services
    builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
    builder.Services.AddTimeTravelService<OrderAggregate>();

    // ==========================================================================
    // 5. Infrastructure
    // ==========================================================================

    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();
    builder.Services.AddObservability(builder.Configuration);
    builder.Services.AddOrderSystemHealthChecks(builder.Configuration);

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new()
        {
            Title = "OrderSystem API",
            Version = "v1",
            Description = "Catga CQRS Best Practices Example"
        });
    });

    // ==========================================================================
    // 6. Build Pipeline
    // ==========================================================================

    var app = builder.Build();

    app.UseExceptionHandler();
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseDefaultFiles();
    app.UseStaticFiles();

    // ==========================================================================
    // 7. Endpoints
    // ==========================================================================

    app.MapOrderSystemHealthChecks();
    app.MapOrderEndpoints();
    app.MapEventSourcingEndpoints();

    // ==========================================================================
    // 8. Run
    // ==========================================================================

    Log.Information("Starting OrderSystem API...");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

namespace OrderSystem.Api { public partial class Program; }
