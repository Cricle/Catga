using System.Diagnostics;
using System.Runtime.CompilerServices;
using Catga.Abstractions;
using Catga.Observability;
using Catga.Resilience;

namespace Catga.EventSourcing;

/// <summary>
/// Base class for event store implementations.
/// Provides common validation, diagnostics, and resilience patterns.
/// </summary>
public abstract class EventStoreBase : IEventStore
{
    protected readonly IResiliencePipelineProvider ResilienceProvider;
    protected readonly IEventTypeRegistry TypeRegistry;

    protected EventStoreBase(
        IResiliencePipelineProvider resilienceProvider,
        IEventTypeRegistry? typeRegistry = null)
    {
        ResilienceProvider = resilienceProvider ?? throw new ArgumentNullException(nameof(resilienceProvider));
        TypeRegistry = typeRegistry ?? new Core.DefaultEventTypeRegistry();
    }

    #region Abstract Methods - Provider Specific

    protected abstract ValueTask AppendCoreAsync(
        string streamId,
        IReadOnlyList<IEvent> events,
        long expectedVersion,
        CancellationToken ct);

    protected abstract ValueTask<EventStream> ReadCoreAsync(
        string streamId,
        long fromVersion,
        int maxCount,
        CancellationToken ct);

    protected abstract ValueTask<long> GetVersionCoreAsync(
        string streamId,
        CancellationToken ct);

    protected abstract ValueTask<EventStream> ReadToVersionCoreAsync(
        string streamId,
        long toVersion,
        CancellationToken ct);

    protected abstract ValueTask<EventStream> ReadToTimestampCoreAsync(
        string streamId,
        DateTime upperBound,
        CancellationToken ct);

    protected abstract ValueTask<IReadOnlyList<VersionInfo>> GetVersionHistoryCoreAsync(
        string streamId,
        CancellationToken ct);

    protected abstract ValueTask<IReadOnlyList<string>> GetAllStreamIdsCoreAsync(
        CancellationToken ct);

    #endregion

    #region IEventStore Implementation

    public async ValueTask AppendAsync(
        string streamId,
        IReadOnlyList<IEvent> events,
        long expectedVersion = -1,
        CancellationToken cancellationToken = default)
    {
        ValidateStreamId(streamId);
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0) return;

        await ResilienceProvider.ExecutePersistenceAsync(async ct =>
        {
            using var scope = DiagnosticsScopeFactory.EventStoreAppend(streamId);
            try
            {
                await AppendCoreAsync(streamId, events, expectedVersion, ct);
                scope.RecordSuccess(events.Count);
            }
            catch (Exception ex)
            {
                scope.SetError(ex);
                scope.RecordFailure();
                throw;
            }
        }, cancellationToken);
    }

    public async ValueTask<EventStream> ReadAsync(
        string streamId,
        long fromVersion = 0,
        int maxCount = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        ValidateStreamId(streamId);

        return await ResilienceProvider.ExecutePersistenceAsync(async ct =>
        {
            using var scope = DiagnosticsScopeFactory.EventStoreRead(streamId);
            try
            {
                var result = await ReadCoreAsync(streamId, fromVersion, maxCount, ct);
                scope.RecordSuccess();
                return result;
            }
            catch (Exception ex)
            {
                scope.SetError(ex);
                scope.RecordFailure();
                throw;
            }
        }, cancellationToken);
    }

    public async ValueTask<long> GetVersionAsync(
        string streamId,
        CancellationToken cancellationToken = default)
    {
        ValidateStreamId(streamId);
        return await ResilienceProvider.ExecutePersistenceAsync(
            ct => GetVersionCoreAsync(streamId, ct),
            cancellationToken);
    }

    public async ValueTask<EventStream> ReadToVersionAsync(
        string streamId,
        long toVersion,
        CancellationToken cancellationToken = default)
    {
        ValidateStreamId(streamId);
        return await ResilienceProvider.ExecutePersistenceAsync(async ct =>
        {
            using var scope = DiagnosticsScopeFactory.EventStoreRead(streamId);
            try
            {
                var result = await ReadToVersionCoreAsync(streamId, toVersion, ct);
                scope.RecordSuccess();
                return result;
            }
            catch (Exception ex)
            {
                scope.SetError(ex);
                throw;
            }
        }, cancellationToken);
    }

    public async ValueTask<EventStream> ReadToTimestampAsync(
        string streamId,
        DateTime upperBound,
        CancellationToken cancellationToken = default)
    {
        ValidateStreamId(streamId);
        return await ResilienceProvider.ExecutePersistenceAsync(async ct =>
        {
            using var scope = DiagnosticsScopeFactory.EventStoreRead(streamId);
            try
            {
                var result = await ReadToTimestampCoreAsync(streamId, upperBound, ct);
                scope.RecordSuccess();
                return result;
            }
            catch (Exception ex)
            {
                scope.SetError(ex);
                throw;
            }
        }, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<VersionInfo>> GetVersionHistoryAsync(
        string streamId,
        CancellationToken cancellationToken = default)
    {
        ValidateStreamId(streamId);
        return await ResilienceProvider.ExecutePersistenceAsync(
            ct => GetVersionHistoryCoreAsync(streamId, ct),
            cancellationToken);
    }

    public ValueTask<IReadOnlyList<string>> GetAllStreamIdsAsync(
        CancellationToken cancellationToken = default)
    {
        return ResilienceProvider.ExecutePersistenceAsync(
            GetAllStreamIdsCoreAsync,
            cancellationToken);
    }

    #endregion

    #region Helper Methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void ValidateStreamId(string streamId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
    }

    protected static EventStream EmptyStream(string streamId) => new()
    {
        StreamId = streamId,
        Version = -1,
        Events = []
    };

    #endregion
}
