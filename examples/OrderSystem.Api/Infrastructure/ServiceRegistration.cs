// OrderSystem.Api - Handler and Service Registration
using Catga.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Handlers;
using OrderSystem.Api.Infrastructure.Caching;
using OrderSystem.Api.Infrastructure.Telemetry;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;
using Polly;
using Polly.Extensions.Http;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AppTelemetry = OrderSystem.Api.Infrastructure.Telemetry.Telemetry;

namespace OrderSystem.Api.Infrastructure;

/// <summary>
/// OrderSystem-specific service registration extensions
/// </summary>
public static class OrderSystemServiceExtensions
{
    /// <summary>
    /// Registers all OrderSystem handlers
    /// </summary>
    public static IServiceCollection AddOrderSystemHandlers(this IServiceCollection services)
    {
        // Request Handlers - Scoped
        services.AddScoped<IRequestHandler<CreateOrderCommand, OrderCreatedResult>, CreateOrderHandler>();
        services.AddScoped<IRequestHandler<CancelOrderCommand>, CancelOrderHandler>();
        services.AddScoped<IRequestHandler<GetOrderQuery, Order?>, GetOrderHandler>();

        // Event Handlers - Scoped
        services.AddScoped<IEventHandler<OrderCreatedEvent>, OrderCreatedNotificationHandler>();
        services.AddScoped<IEventHandler<OrderCreatedEvent>, OrderCreatedAnalyticsHandler>();
        services.AddScoped<IEventHandler<OrderCancelledEvent>, OrderCancelledHandler>();
        services.AddScoped<IEventHandler<OrderFailedEvent>, OrderFailedHandler>();

        return services;
    }

    /// <summary>
    /// Registers all OrderSystem services with resilience, caching, and telemetry
    /// </summary>
    public static IServiceCollection AddOrderSystemServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure Redis
        var redisOptions = configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>() ?? new RedisOptions();

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisOptions.ConnectionString;
            options.InstanceName = redisOptions.InstanceName;
        });

        // Register core services with proper scoping
        services.AddSingleton<InMemoryOrderRepository>(); // The concrete implementation
        services.AddScoped<IOrderRepository>(sp =>
            new CachedOrderRepository(
                sp.GetRequiredService<InMemoryOrderRepository>(),
                sp.GetRequiredService<IDistributedCache>(),
                sp.GetRequiredService<ILogger<CachedOrderRepository>>()));

        // Register inventory service with resilience
        services.AddHttpClient<IInventoryService, MockInventoryService>()
            .AddPolicyHandler(GetRetryPolicy())
            .AddPolicyHandler(GetCircuitBreakerPolicy());

        // Register payment service with resilience
        services.AddHttpClient<IPaymentService, MockPaymentService>()
            .AddPolicyHandler(GetRetryPolicy())
            .AddPolicyHandler(GetCircuitBreakerPolicy());

        // Configure OpenTelemetry
        var serviceName = "OrderSystem.Api";
        var serviceVersion = "1.0.0";
        var otlpEndpoint = configuration["OpenTelemetry:Endpoint"] ?? "http://localhost:4317";

        services.AddOpenTelemetry()
            .WithTracing(tracerProviderBuilder =>
            {
                tracerProviderBuilder
                    .AddSource(AppTelemetry.ActivitySource.Name)
                    .ConfigureResource(resource => resource
                        .AddService(serviceName, serviceVersion: serviceVersion))
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(otlpOptions =>
                    {
                        otlpOptions.Endpoint = new Uri(otlpEndpoint);
                    });
            })
            .WithMetrics(metricsProviderBuilder =>
            {
                metricsProviderBuilder
                    .AddMeter(AppTelemetry.Meter.Name)
                    .ConfigureResource(resource => resource
                        .AddService(serviceName, serviceVersion: serviceVersion))
                    .AddAspNetCoreInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddOtlpExporter(otlpOptions =>
                    {
                        otlpOptions.Endpoint = new Uri(otlpEndpoint);
                    });
            });

        // Configure health checks
        services.AddHealthChecks();

        return services;
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt =>
                    TimeSpan.FromMilliseconds(200 * Math.Pow(2, retryAttempt - 1) + Random.Shared.Next(0, 100)),
                onRetry: (outcome, delay, retryAttempt, context) =>
                {
                    var logger = context.GetLogger();
                    logger?.LogWarning("Retry attempt {RetryAttempt} after {Delay}ms for {RequestUri}",
                        retryAttempt,
                        delay.TotalMilliseconds,
                        outcome.Result?.RequestMessage?.RequestUri);
                });
    }

    private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (outcome, breakDelay, context) =>
                {
                    var logger = context.GetLogger();
                    logger?.LogWarning("Circuit breaker opened for {Duration}ms due to {Exception}",
                        breakDelay.TotalMilliseconds,
                        outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
                },
                onReset: context =>
                {
                    var logger = context.GetLogger();
                    logger?.LogInformation("Circuit breaker reset");
                },
                onHalfOpen: () =>
                {
                    // No-op - can be used for additional logging if needed
                });
    }

    private static class PolicyContextItems
    {
        public const string Logger = "Logger";
    }

    private static ILogger? GetLogger(this Context context)
    {
        if (context.TryGetValue(PolicyContextItems.Logger, out var logger) && logger is ILogger loggerInstance)
        {
            return loggerInstance;
        }
        return null;
    }
}
