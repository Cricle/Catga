using Microsoft.Extensions.DependencyInjection;

namespace Catga.EventSourcing;

/// <summary>
/// Extension methods for adding event sourcing to the service collection
/// </summary>
public static class EventSourcingServiceCollectionExtensions
{
    /// <summary>
    /// Add in-memory event store
    /// </summary>
    public static IServiceCollection AddMemoryEventStore(
        this IServiceCollection services)
    {
        services.AddSingleton<IEventStore, MemoryEventStore>();
        return services;
    }
}

