using Catga.Abstractions;
using Catga.DeadLetter;
using Catga.EventSourcing;
using Catga.Inbox;
using Catga.Outbox;
using Catga.Persistence.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.DependencyInjection;

/// <summary>
/// Extension methods for setting up InMemory persistence services in an <see cref="IServiceCollection" />.
/// InMemory implementations are ideal for development, testing, and single-node scenarios.
/// </summary>
public static class InMemoryPersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Adds InMemory event store to the service collection.
    /// </summary>
    public static IServiceCollection AddInMemoryEventStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IEventStore, InMemoryEventStore>();
        return services;
    }

    /// <summary>
    /// Adds InMemory outbox store to the service collection.
    /// </summary>
    public static IServiceCollection AddInMemoryOutboxStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IOutboxStore, MemoryOutboxStore>();
        return services;
    }

    /// <summary>
    /// Adds InMemory inbox store to the service collection.
    /// </summary>
    public static IServiceCollection AddInMemoryInboxStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IInboxStore, MemoryInboxStore>();
        return services;
    }

    /// <summary>
    /// Adds InMemory dead letter queue to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="maxSize">Maximum number of dead letters to keep (default: 1000)</param>
    public static IServiceCollection AddInMemoryDeadLetterQueue(
        this IServiceCollection services,
        int maxSize = 1000)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IDeadLetterQueue>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemoryDeadLetterQueue>>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            return new InMemoryDeadLetterQueue(logger, serializer, maxSize);
        });
        return services;
    }

    /// <summary>
    /// Adds complete InMemory persistence (EventStore + Outbox + Inbox + DeadLetterQueue) to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="deadLetterMaxSize">Maximum number of dead letters to keep (default: 1000)</param>
    public static IServiceCollection AddInMemoryPersistence(
        this IServiceCollection services,
        int deadLetterMaxSize = 1000)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddInMemoryEventStore();
        services.AddInMemoryOutboxStore();
        services.AddInMemoryInboxStore();
        services.AddInMemoryDeadLetterQueue(deadLetterMaxSize);

        return services;
    }
}

