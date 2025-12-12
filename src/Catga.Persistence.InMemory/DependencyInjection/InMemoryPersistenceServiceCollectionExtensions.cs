using Catga.Abstractions;
using Catga.DeadLetter;
using Catga.EventSourcing;
using Catga.Flow;
using Catga.Inbox;
using Catga.Observability;
using Catga.Outbox;
using Catga.Persistence.InMemory.Flow;
using Catga.Persistence.InMemory.Stores;
using Catga.Persistence.Stores;
using Catga.Resilience;
using Catga.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Diagnostics;
using Catga.Idempotency;
using Catga.Persistence.InMemory.Locking;
using Microsoft.Extensions.Options;

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
        var sw = Stopwatch.StartNew();
        var tag = new KeyValuePair<string, object?>("component", "DI.Persistence.InMemory.EventStore");
        try
        {
            services.TryAddSingleton<IEventStore>(sp =>
            {
                var provider = sp.GetRequiredService<IResiliencePipelineProvider>();
                return new InMemoryEventStore(provider);
            });
            sw.Stop();
            CatgaDiagnostics.DIRegistrationsCompleted.Add(1, tag);
            return services;
        }
        catch
        {
            sw.Stop();
            CatgaDiagnostics.DIRegistrationsFailed.Add(1, tag);
            throw;
        }
        finally
        {
            CatgaDiagnostics.DIRegistrationDuration.Record(sw.Elapsed.TotalMilliseconds, tag);
        }
    }

    /// <summary>
    /// Adds InMemory outbox store to the service collection.
    /// </summary>
    public static IServiceCollection AddInMemoryOutboxStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var sw = Stopwatch.StartNew();
        var tag = new KeyValuePair<string, object?>("component", "DI.Persistence.InMemory.Outbox");
        try
        {
            services.TryAddSingleton<IOutboxStore>(sp =>
            {
                var provider = sp.GetRequiredService<IResiliencePipelineProvider>();
                return new MemoryOutboxStore(provider);
            });
            sw.Stop();
            CatgaDiagnostics.DIRegistrationsCompleted.Add(1, tag);
            return services;
        }
        catch
        {
            sw.Stop();
            CatgaDiagnostics.DIRegistrationsFailed.Add(1, tag);
            throw;
        }
        finally
        {
            CatgaDiagnostics.DIRegistrationDuration.Record(sw.Elapsed.TotalMilliseconds, tag);
        }
    }

    /// <summary>
    /// Adds InMemory inbox store to the service collection.
    /// </summary>
    public static IServiceCollection AddInMemoryInboxStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var sw = Stopwatch.StartNew();
        var tag = new KeyValuePair<string, object?>("component", "DI.Persistence.InMemory.Inbox");
        try
        {
            services.TryAddSingleton<IInboxStore>(sp =>
            {
                var provider = sp.GetRequiredService<IResiliencePipelineProvider>();
                return new MemoryInboxStore(provider);
            });
            sw.Stop();
            CatgaDiagnostics.DIRegistrationsCompleted.Add(1, tag);
            return services;
        }
        catch
        {
            sw.Stop();
            CatgaDiagnostics.DIRegistrationsFailed.Add(1, tag);
            throw;
        }
        finally
        {
            CatgaDiagnostics.DIRegistrationDuration.Record(sw.Elapsed.TotalMilliseconds, tag);
        }
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
        var sw = Stopwatch.StartNew();
        var tag = new KeyValuePair<string, object?>("component", "DI.Persistence.InMemory.DLQ");
        try
        {
            services.TryAddSingleton<IDeadLetterQueue>(sp =>
            {
                var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemoryDeadLetterQueue>>();
                var serializer = sp.GetRequiredService<IMessageSerializer>();
                return new InMemoryDeadLetterQueue(logger, serializer, maxSize);
            });
            sw.Stop();
            CatgaDiagnostics.DIRegistrationsCompleted.Add(1, tag);
            return services;
        }
        catch
        {
            sw.Stop();
            CatgaDiagnostics.DIRegistrationsFailed.Add(1, tag);
            throw;
        }
        finally
        {
            CatgaDiagnostics.DIRegistrationDuration.Record(sw.Elapsed.TotalMilliseconds, tag);
        }
    }

    /// <summary>
    /// Adds InMemory idempotency store to the service collection.
    /// </summary>
    public static IServiceCollection AddInMemoryIdempotencyStore(this IServiceCollection services, Action<InMemoryIdempotencyStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (configure != null)
            services.Configure(configure);
        services.TryAddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        return services;
    }

    /// <summary>
    /// Adds InMemory rate limiter to the service collection.
    /// </summary>
    public static IServiceCollection AddInMemoryRateLimiter(this IServiceCollection services, Action<InMemoryRateLimiterOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (configure != null)
            services.Configure(configure);
        services.TryAddSingleton<IDistributedRateLimiter, InMemoryRateLimiter>();
        return services;
    }

    /// <summary>
    /// Adds InMemory message scheduler to the service collection.
    /// </summary>
    public static IServiceCollection AddInMemoryMessageScheduler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IMessageScheduler>(sp => new InMemoryMessageScheduler(sp.GetRequiredService<ICatgaMediator>()));
        return services;
    }

    /// <summary>
    /// Adds InMemory snapshot store to the service collection.
    /// </summary>
    public static IServiceCollection AddInMemorySnapshotStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ISnapshotStore, InMemorySnapshotStore>();
        return services;
    }

    /// <summary>
    /// Adds InMemory enhanced snapshot store with version history support.
    /// </summary>
    public static IServiceCollection AddInMemoryEnhancedSnapshotStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IEnhancedSnapshotStore>(sp =>
            new InMemoryEnhancedSnapshotStore(sp.GetRequiredService<IMessageSerializer>()));
        // Also register as ISnapshotStore for compatibility
        services.TryAddSingleton<ISnapshotStore>(sp => sp.GetRequiredService<IEnhancedSnapshotStore>());
        return services;
    }

    /// <summary>
    /// Adds InMemory distributed lock to the service collection.
    /// </summary>
    public static IServiceCollection AddInMemoryDistributedLock(this IServiceCollection services, Action<DistributedLockOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (configure != null)
            services.Configure(configure);
        else
            services.TryAddSingleton(Options.Create(new DistributedLockOptions()));
        services.TryAddSingleton<IDistributedLock, InMemoryDistributedLock>();
        return services;
    }

    /// <summary>
    /// Adds InMemory flow store to the service collection.
    /// </summary>
    public static IServiceCollection AddInMemoryFlowStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IFlowStore, InMemoryFlowStore>();
        return services;
    }

    /// <summary>
    /// Adds InMemory DSL flow store to the service collection.
    /// </summary>
    public static IServiceCollection AddInMemoryDslFlowStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<Catga.Flow.Dsl.IDslFlowStore>(sp =>
            new InMemoryDslFlowStore(sp.GetRequiredService<IMessageSerializer>()));
        return services;
    }

    /// <summary>
    /// Adds complete InMemory persistence (all stores) to the service collection.
    /// </summary>
    public static IServiceCollection AddInMemoryPersistence(
        this IServiceCollection services,
        int deadLetterMaxSize = 1000)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddInMemoryEventStore();
        services.AddInMemoryOutboxStore();
        services.AddInMemoryInboxStore();
        services.AddInMemoryDeadLetterQueue(deadLetterMaxSize);
        services.AddInMemoryIdempotencyStore();
        services.AddInMemorySnapshotStore();
        services.AddInMemoryDistributedLock();
        services.AddInMemoryFlowStore();

        return services;
    }
}

