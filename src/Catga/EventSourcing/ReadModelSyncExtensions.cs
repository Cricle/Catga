using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.EventSourcing;

/// <summary>
/// DI extension methods for Read Model Synchronization.
/// </summary>
public static class ReadModelSyncExtensions
{
    /// <summary>
    /// Adds Read Model Sync services with realtime strategy (no-op default).
    /// </summary>
    public static IServiceCollection AddReadModelSync(this IServiceCollection services)
    {
        services.TryAddSingleton<IChangeTracker, InMemoryChangeTracker>();
        services.TryAddSingleton<ISyncStrategy>(_ => new RealtimeSyncStrategy(_ => ValueTask.CompletedTask));
        services.TryAddSingleton<IReadModelSynchronizer, DefaultReadModelSynchronizer>();

        return services;
    }

    /// <summary>
    /// Adds Read Model Sync services with realtime strategy and custom action.
    /// </summary>
    public static IServiceCollection AddReadModelSync(
        this IServiceCollection services,
        Func<ChangeRecord, ValueTask> syncAction)
    {
        services.TryAddSingleton<IChangeTracker, InMemoryChangeTracker>();
        services.TryAddSingleton<ISyncStrategy>(_ => new RealtimeSyncStrategy(syncAction));
        services.TryAddSingleton<IReadModelSynchronizer, DefaultReadModelSynchronizer>();

        return services;
    }

    /// <summary>
    /// Adds Read Model Sync services with custom tracker.
    /// </summary>
    public static IServiceCollection AddReadModelSync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TTracker>(
        this IServiceCollection services,
        Func<ChangeRecord, ValueTask> syncAction)
        where TTracker : class, IChangeTracker
    {
        services.TryAddSingleton<IChangeTracker, TTracker>();
        services.TryAddSingleton<ISyncStrategy>(_ => new RealtimeSyncStrategy(syncAction));
        services.TryAddSingleton<IReadModelSynchronizer, DefaultReadModelSynchronizer>();

        return services;
    }

    /// <summary>
    /// Adds Read Model Sync with batch strategy.
    /// </summary>
    public static IServiceCollection AddReadModelSyncWithBatching(
        this IServiceCollection services,
        int batchSize,
        Func<IReadOnlyList<ChangeRecord>, ValueTask> batchAction)
    {
        services.TryAddSingleton<IChangeTracker, InMemoryChangeTracker>();
        services.AddSingleton<ISyncStrategy>(_ => new BatchSyncStrategy(batchSize, batchAction));
        services.TryAddSingleton<IReadModelSynchronizer, DefaultReadModelSynchronizer>();

        return services;
    }

    /// <summary>
    /// Adds Read Model Sync with scheduled strategy.
    /// </summary>
    public static IServiceCollection AddReadModelSyncWithSchedule(
        this IServiceCollection services,
        TimeSpan interval,
        Func<IReadOnlyList<ChangeRecord>, ValueTask> syncAction)
    {
        services.TryAddSingleton<IChangeTracker, InMemoryChangeTracker>();
        services.AddSingleton<ISyncStrategy>(_ => new ScheduledSyncStrategy(interval, syncAction));
        services.TryAddSingleton<IReadModelSynchronizer, DefaultReadModelSynchronizer>();

        return services;
    }

    /// <summary>
    /// Adds a projection-based read model synchronizer.
    /// </summary>
    public static IServiceCollection AddProjectionSync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProjection>(this IServiceCollection services)
        where TProjection : class, IProjection
    {
        services.TryAddSingleton<IProjection, TProjection>();
        services.TryAddSingleton<IChangeTracker, InMemoryChangeTracker>();
        services.AddSingleton<ISyncStrategy>(sp =>
        {
            var projection = sp.GetRequiredService<IProjection>();
            return new RealtimeSyncStrategy(async change =>
            {
                await projection.ApplyAsync(change.Event);
            });
        });
        services.TryAddSingleton<IReadModelSynchronizer, DefaultReadModelSynchronizer>();

        return services;
    }
}
