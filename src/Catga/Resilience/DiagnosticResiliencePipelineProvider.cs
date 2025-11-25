using System;
using System.Threading;
using System.Threading.Tasks;

namespace Catga.Resilience;

public sealed class DiagnosticResiliencePipelineProvider : IResiliencePipelineProvider
{
    public ValueTask<T> ExecuteMediatorAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken)
        => action(cancellationToken);

    public ValueTask ExecuteMediatorAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken)
        => action(cancellationToken);

    public ValueTask<T> ExecuteTransportPublishAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken)
        => action(cancellationToken);

    public ValueTask ExecuteTransportPublishAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken)
        => action(cancellationToken);

    public ValueTask<T> ExecuteTransportSendAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken)
        => action(cancellationToken);

    public ValueTask ExecuteTransportSendAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken)
        => action(cancellationToken);

    public ValueTask<T> ExecutePersistenceAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken)
        => action(cancellationToken);

    public ValueTask ExecutePersistenceAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken)
        => action(cancellationToken);
}
