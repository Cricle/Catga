using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.InMemory.Stores;
using Catga.Persistence.Stores;
using Catga.Pipeline;
using OrderSystem.Api.Behaviors;
using OrderSystem.Api.Domain;
using OrderSystem.Api.EventSourcing;
using OrderSystem.Api.Handlers;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Extensions;

/// <summary>
/// Extension methods for service registration.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add all OrderSystem handlers.
    /// </summary>
    public static IServiceCollection AddOrderSystemHandlers(this IServiceCollection services)
    {
        // Command/Query Handlers
        services.AddSingleton<IRequestHandler<CreateOrderCommand, OrderCreatedResult>, CreateOrderHandler>();
        services.AddSingleton<IRequestHandler<CreateOrderFlowCommand, OrderCreatedResult>, CreateOrderFlowHandler>();
        services.AddSingleton<IRequestHandler<CancelOrderCommand>, CancelOrderHandler>();
        services.AddSingleton<IRequestHandler<GetOrderQuery, Order?>, GetOrderHandler>();
        services.AddSingleton<IRequestHandler<GetUserOrdersQuery, List<Order>>, GetUserOrdersHandler>();

        // Event Handlers
        services.AddSingleton<IEventHandler<OrderCreatedEvent>, OrderCreatedEventHandler>();
        services.AddSingleton<IEventHandler<OrderCreatedEvent>, SendOrderNotificationHandler>();
        services.AddSingleton<IEventHandler<OrderCancelledEvent>, OrderCancelledEventHandler>();
        services.AddSingleton<IEventHandler<OrderConfirmedEvent>, OrderConfirmedEventHandler>();

        return services;
    }

    /// <summary>
    /// Add pipeline behaviors.
    /// </summary>
    public static IServiceCollection AddOrderSystemBehaviors(this IServiceCollection services)
    {
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        return services;
    }

    /// <summary>
    /// Add event sourcing services.
    /// </summary>
    public static IServiceCollection AddOrderSystemEventSourcing(this IServiceCollection services)
    {
        // Projections
        services.AddSingleton<OrderSummaryProjection>();
        services.AddSingleton<CustomerStatsProjection>();
        services.AddSingleton<InMemoryProjectionCheckpointStore>();

        // Subscriptions
        services.AddSingleton<InMemorySubscriptionStore>();
        services.AddSingleton<OrderEventSubscriptionHandler>();
        services.AddSingleton<OrderNotificationHandler>();

        // Event Versioning
        services.AddOrderEventVersioning();

        // Audit & Compliance
        services.AddSingleton<InMemoryAuditLogStore>();
        services.AddSingleton<IAuditLogStore>(sp => sp.GetRequiredService<InMemoryAuditLogStore>());
        services.AddSingleton<InMemoryGdprStore>();
        services.AddSingleton<IGdprStore>(sp => sp.GetRequiredService<InMemoryGdprStore>());
        services.AddSingleton<OrderAuditService>();

        // Enhanced Snapshots
        services.AddSingleton<EnhancedInMemorySnapshotStore>();
        services.AddSingleton<IEnhancedSnapshotStore>(sp => sp.GetRequiredService<EnhancedInMemorySnapshotStore>());

        return services;
    }
}
