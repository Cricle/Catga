namespace Catga.Resilience;

public interface IResiliencePipelineProvider
{
    // Mediator operations (no retry by default)
    ValueTask<T> ExecuteMediatorAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken);
    ValueTask ExecuteMediatorAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken);

    // Transport operations (with retry for transient failures)
    ValueTask<T> ExecuteTransportPublishAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken);
    ValueTask ExecuteTransportPublishAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken);
    ValueTask<T> ExecuteTransportSendAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken);
    ValueTask ExecuteTransportSendAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken);

    // Persistence operations with retry (for idempotent read/write operations)
    ValueTask<T> ExecutePersistenceAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken);
    ValueTask ExecutePersistenceAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken);

    // Persistence operations without retry (for non-idempotent operations like locks, optimistic concurrency)
    ValueTask<T> ExecutePersistenceNoRetryAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken);
    ValueTask ExecutePersistenceNoRetryAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken);
}
