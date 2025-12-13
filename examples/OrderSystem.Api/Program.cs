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
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OrderSystem.Api.Configuration;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Endpoints;
using OrderSystem.Api.Infrastructure;
using OrderSystem.Api.Services;
using Serilog;
using System.Text;

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

    // Order Repository: InMemory (default) | SQLite
    if (catgaOptions.Persistence.Equals("sqlite", StringComparison.OrdinalIgnoreCase))
    {
        var sqliteConnection = catgaOptions.SqliteConnection ?? "Data Source=orders.db";
        builder.Services.AddSingleton<IOrderRepository>(sp => new SqliteOrderRepository(sqliteConnection));
        Log.Information("Using SQLite repository: {Connection}", sqliteConnection);
    }
    else
    {
        builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
    }
    builder.Services.AddTimeTravelService<OrderAggregate>();

    // Payment processor
    builder.Services.AddSingleton<PaymentProcessor>();

    // Authentication service
    builder.Services.AddSingleton<AuthenticationService>();

    // JWT Authentication
    var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "your-secret-key-minimum-32-characters-long-for-hs256";
    var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "OrderSystem.Api";
    var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "OrderSystem.Client";

    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(jwtSecret)),
                ValidateIssuer = true,
                ValidIssuer = jwtIssuer,
                ValidateAudience = true,
                ValidAudience = jwtAudience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
        });

    builder.Services.AddAuthorization();

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

    // Authentication & Authorization middleware
    app.UseAuthentication();
    app.UseAuthorization();

    // ==========================================================================
    // 7. Endpoints
    // ==========================================================================

    app.MapOrderSystemHealthChecks();
    app.MapAuthEndpoints();
    app.MapOrderEndpoints();
    app.MapPaymentEndpoints();
    app.MapEventSourcingEndpoints();

    // System Info endpoint
    app.MapGet("/api/system/info", () => new
    {
        Framework = "Catga CQRS",
        Version = "1.0.0",
        Transport = catgaOptions.Transport,
        Persistence = catgaOptions.Persistence,
        ClusterEnabled = catgaOptions.ClusterEnabled,
        ClusterNodes = catgaOptions.ClusterNodes,
        Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        Environment = app.Environment.EnvironmentName,
        MachineName = Environment.MachineName,
        ProcessorCount = Environment.ProcessorCount,
        AotCompatible = true
    }).WithTags("System");

    // SPA fallback - serve index.html for client-side routing
    app.MapFallbackToFile("index.html");

    // ==========================================================================
    // 8. Run
    // ==========================================================================

    Log.Information("Starting OrderSystem API with Transport={Transport}, Persistence={Persistence}...",
        catgaOptions.Transport, catgaOptions.Persistence);
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
