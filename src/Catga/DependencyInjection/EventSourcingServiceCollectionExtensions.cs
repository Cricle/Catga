using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Catga.EventSourcing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.DependencyInjection;

/// <summary>
/// Extension methods for setting up Event Sourcing services in an <see cref="IServiceCollection" />.
/// </summary>
public static class EventSourcingServiceCollectionExtensions
{
    /// <summary>
    /// Adds Event Sourcing core services including event type registry and default implementations.
    /// </summary>
    public static IServiceCollection AddEventSourcing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Event type registry for type resolution
        services.TryAddSingleton<IEventTypeRegistry, DefaultEventTypeRegistry>();

        // Event version registry for schema evolution
        services.TryAddSingleton<IEventVersionRegistry, EventVersionRegistry>();

        return services;
    }

    /// <summary>
    /// Adds a projection to the service collection.
    /// </summary>
    public static IServiceCollection AddProjection<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProjection>(this IServiceCollection services)
        where TProjection : class, IProjection
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<TProjection>();
        return services;
    }

    /// <summary>
    /// Adds an event upgrader for schema evolution.
    /// </summary>
    public static IServiceCollection AddEventUpgrader<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TUpgrader>(this IServiceCollection services)
        where TUpgrader : class, IEventUpgrader
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IEventUpgrader, TUpgrader>();
        return services;
    }

    /// <summary>
    /// Adds time travel service with optional snapshot support for the specified aggregate type.
    /// </summary>
    public static IServiceCollection AddTimeTravelService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        this IServiceCollection services,
        bool useSnapshots = false)
        where TAggregate : class, IAggregateRoot, new()
    {
        ArgumentNullException.ThrowIfNull(services);

        if (useSnapshots)
        {
            services.TryAddSingleton<ITimeTravelService<TAggregate>>(sp =>
                new TimeTravelServiceWithSnapshots<TAggregate>(
                    sp.GetRequiredService<IEventStore>(),
                    sp.GetRequiredService<IEnhancedSnapshotStore>()));
        }
        else
        {
            services.TryAddSingleton<ITimeTravelService<TAggregate>, TimeTravelService<TAggregate>>();
        }

        return services;
    }

    /// <summary>
    /// Adds subscription runner for processing persistent subscriptions.
    /// </summary>
    public static IServiceCollection AddSubscriptionRunner<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(this IServiceCollection services)
        where THandler : class, IEventHandler
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<THandler>();
        return services;
    }

    /// <summary>
    /// Adds projection rebuilder service for rebuilding projections from events.
    /// </summary>
    public static IServiceCollection AddProjectionRebuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProjection>(this IServiceCollection services)
        where TProjection : class, IProjection
    {
        ArgumentNullException.ThrowIfNull(services);

        // Ensure projection is registered
        services.TryAddSingleton<TProjection>();

        // Register rebuilder factory
        services.TryAddSingleton(sp =>
        {
            var eventStore = sp.GetRequiredService<IEventStore>();
            var checkpointStore = sp.GetRequiredService<IProjectionCheckpointStore>();
            var projection = sp.GetRequiredService<TProjection>();
            return new ProjectionRebuilder<TProjection>(eventStore, checkpointStore, projection, projection.Name);
        });

        return services;
    }

    /// <summary>
    /// Adds audit services including audit log store and immutability verification.
    /// </summary>
    public static IServiceCollection AddAuditServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // ImmutabilityVerifier is created per-use, no DI registration needed
        // GdprService is created per-use, no DI registration needed

        return services;
    }
}
