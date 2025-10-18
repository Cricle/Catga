using Catga.EventSourcing;
using Catga.InMemory.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.InMemory.DependencyInjection;

/// <summary>
/// Service collection extensions for event sourcing
/// </summary>
public static class EventSourcingServiceCollectionExtensions
{
    /// <summary>
    /// Add in-memory event store (for testing and single-node scenarios)
    /// </summary>
    public static IServiceCollection AddInMemoryEventStore(this IServiceCollection services)
    {
        services.TryAddSingleton<IEventStore, InMemoryEventStore>();
        return services;
    }

    /// <summary>
    /// Add event store repository for loading and saving aggregates
    /// </summary>
    public static IServiceCollection AddEventStoreRepository(this IServiceCollection services)
    {
        services.TryAddScoped(typeof(IEventStoreRepository<,>), typeof(EventStoreRepository<,>));
        return services;
    }
}

