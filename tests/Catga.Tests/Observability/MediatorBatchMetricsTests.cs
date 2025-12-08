using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics.Metrics;
using Catga.Abstractions;
using Catga.Core;
using Catga.Observability;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Observability;

public class MediatorBatchMetricsTests
{
    [Fact]
    public async Task OverflowCounter_Increments_WhenQueueOverflows()
    {
        ObservabilityHooks.Enable();

        using var listener = new MeterListener();
        long overflowCount = 0;
        listener.InstrumentPublished = (inst, l) =>
        {
            if (inst.Meter.Name == CatgaDiagnostics.MeterName && inst.Name == "catga.mediator.batch.overflow")
            {
                l.EnableMeasurementEvents(inst);
            }
        };
        listener.SetMeasurementEventCallback<long>((inst, measurement, tags, state) =>
        {
            if (inst.Name == "catga.mediator.batch.overflow")
            {
                Interlocked.Add(ref overflowCount, measurement);
            }
        });
        listener.Start();

        var logger = Substitute.For<ILogger<AutoBatchingBehavior<OverflowMetricReq, int>>>();
        var options = new MediatorBatchOptions
        {
            EnableAutoBatching = true,
            MaxBatchSize = 1000,
            BatchTimeout = TimeSpan.FromSeconds(5),
            MaxQueueLength = 0 // force immediate overflow on first enqueue
        };
        var provider = new MediatorBatchOptions();
        var behavior = new AutoBatchingBehavior<OverflowMetricReq, int>(logger, options, new NoopResilienceProvider());

        PipelineDelegate<int> next = () => new ValueTask<CatgaResult<int>>(CatgaResult<int>.Success(1));
        var result = await behavior.HandleAsync(new OverflowMetricReq(), next);
        result.IsSuccess.Should().BeFalse();

        // Allow metric event to propagate
        await Task.Delay(50);
        Interlocked.Read(ref overflowCount).Should().BeGreaterOrEqualTo(1);
    }

    public record OverflowMetricReq() : IRequest<int>
    {
        public long MessageId { get; init; }
    }

    private sealed class NoopResilienceProvider : IResiliencePipelineProvider
    {
        public ValueTask ExecuteMediatorAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken) => action(cancellationToken);
        public ValueTask<T> ExecuteMediatorAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken) => action(cancellationToken);
        public ValueTask ExecutePersistenceAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken) => action(cancellationToken);
        public ValueTask<T> ExecutePersistenceAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken) => action(cancellationToken);
        public ValueTask ExecuteTransportPublishAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken) => action(cancellationToken);
        public ValueTask<T> ExecuteTransportPublishAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken) => action(cancellationToken);
        public ValueTask ExecuteTransportSendAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken) => action(cancellationToken);
        public ValueTask<T> ExecuteTransportSendAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken) => action(cancellationToken);
    }
}






