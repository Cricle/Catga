// OrderSystem.Api - Handler and Service Registration
using Catga.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Handlers;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;
using Catga.Abstractions;

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
    /// Registers all OrderSystem services
    /// </summary>
    public static IServiceCollection AddOrderSystemServices(this IServiceCollection services)
    {
        // Application Services - Singleton
        services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
        services.AddSingleton<IInventoryService, MockInventoryService>();
        services.AddSingleton<IPaymentService, MockPaymentService>();

        return services;
    }
}
