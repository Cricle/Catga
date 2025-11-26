using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Catga.Abstractions;
using Catga.Core;
using Catga.Pipeline;
using Catga.Observability;
using Catga.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace Catga.Pipeline.Behaviors;

public sealed class AutoBatchingBehavior<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse
> : BaseBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static Aggregator? _aggregator; // per generic type
    private static readonly object _initLock = new();

    private readonly MediatorBatchOptions _options;
    private readonly IResiliencePipelineProvider _provider;

    public AutoBatchingBehavior(ILogger<AutoBatchingBehavior<TRequest, TResponse>> logger, MediatorBatchOptions options, IResiliencePipelineProvider provider, IHostApplicationLifetime appLifetime)
        : base(logger)
    {
        _options = options ?? new MediatorBatchOptions();
        _provider = provider;
        if (_options.EnableAutoBatching)
        {
            EnsureAggregatorInitialized(_options, _provider, Logger, appLifetime.ApplicationStopping);
        }
    }

    // Fallback for environments without IHostApplicationLifetime (unit tests, simple ServiceCollection)
    public AutoBatchingBehavior(ILogger<AutoBatchingBehavior<TRequest, TResponse>> logger, MediatorBatchOptions options, IResiliencePipelineProvider provider)
        : base(logger)
    {
        _options = options ?? new MediatorBatchOptions();
        _provider = provider;
        if (_options.EnableAutoBatching)
        {
            EnsureAggregatorInitialized(_options, _provider, Logger, CancellationToken.None);
        }
    }

    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        if (!_options.EnableAutoBatching || _options.MaxBatchSize <= 1)
        {
            return await next();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            var tcs = new TaskCompletionSource<CatgaResult<TResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
            tcs.SetCanceled(cancellationToken);
            return await tcs.Task;
        }

        var aggregator = _aggregator!; // initialized in ctor when enabled
        return await aggregator.EnqueueAsync(request, next, cancellationToken);
    }

    private static void EnsureAggregatorInitialized(MediatorBatchOptions options, IResiliencePipelineProvider provider, ILogger logger, CancellationToken stop)
    {
        if (_aggregator != null) return;
        lock (_initLock)
        {
            if (_aggregator == null)
            {
                _aggregator = new Aggregator(options, provider, logger, stop);
            }
        }
    }

    private sealed class Aggregator
    {
        private readonly ConcurrentDictionary<string, Shard> _shards = new();
        private readonly MediatorBatchOptions _options;
        private readonly IResiliencePipelineProvider _provider;
        private readonly ILogger _logger;
        private readonly Timer _timer;
        private readonly CancellationToken _stop;
        private readonly CancellationTokenRegistration _stopReg;

        public Aggregator(MediatorBatchOptions options, IResiliencePipelineProvider provider, ILogger logger, CancellationToken stop)
        {
            _options = options;
            _provider = provider;
            _logger = logger;
            _stop = stop;
            var ms = Math.Max(1.0, options.BatchTimeout.TotalMilliseconds);
            var jitter = 1.0 + (Random.Shared.NextDouble() * 0.2 - 0.1);
            var period = TimeSpan.FromMilliseconds(Math.Max(1, ms * jitter));
            _timer = new Timer(OnTimer, null, period, period);
            _stopReg = _stop.Register(static s => ((Timer)s!).Dispose(), _timer);
        }

        public ValueTask<CatgaResult<TResponse>> EnqueueAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<CatgaResult<TResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entry = new Entry(request, next, tcs, ct);

            var key = (request is Catga.Abstractions.IBatchKeyProvider kp && !string.IsNullOrEmpty(kp.BatchKey)) ? kp.BatchKey! : "_";
            var shard = _shards.GetOrAdd(key, k => new Shard(k, _options, _provider, _logger));
            var newCount = shard.Enqueue(entry);
            if (newCount >= _options.MaxBatchSize) _ = Task.Run(shard.FlushAsync);

            return new ValueTask<CatgaResult<TResponse>>(tcs.Task);
        }

        private void OnTimer(object? state)
        {
            if (_stop.IsCancellationRequested) return;
            foreach (var kv in _shards)
            {
                var shard = kv.Value;
                if (shard.Count > 0) _ = Task.Run(shard.FlushAsync);
            }
        }

        private sealed class Shard
        {
            private readonly string _key;
            private readonly ConcurrentQueue<Entry> _queue = new();
            private readonly MediatorBatchOptions _options;
            private readonly IResiliencePipelineProvider _provider;
            private readonly ILogger _logger;
            private int _count;
            private int _flushing;

            public Shard(string key, MediatorBatchOptions options, IResiliencePipelineProvider provider, ILogger logger)
            {
                _key = key;
                _options = options;
                _provider = provider;
                _logger = logger;
            }

            public int Count => Volatile.Read(ref _count);

            public int Enqueue(Entry entry)
            {
                var newCount = Interlocked.Increment(ref _count);
                _queue.Enqueue(entry);
                if (newCount > _options.MaxQueueLength)
                {
                    if (_queue.TryDequeue(out var dropped))
                    {
                        Interlocked.Decrement(ref _count);
                        dropped.TrySetFailure(CatgaResult<TResponse>.Failure("Mediator batch queue overflow"));
                    }
                }
                return newCount;
            }

            public async Task FlushAsync()
            {
                if (Interlocked.Exchange(ref _flushing, 1) == 1) return;
                try
                {
                    var batch = new List<Entry>(_options.MaxBatchSize);
                    while (batch.Count < _options.MaxBatchSize && _queue.TryDequeue(out var item))
                    {
                        Interlocked.Decrement(ref _count);
                        batch.Add(item);
                    }
                    if (batch.Count == 0) return;
                    var start = Stopwatch.GetTimestamp();
                    try
                    {
                        await _provider.ExecuteMediatorAsync(async ct =>
                        {
                            foreach (var e in batch)
                            {
                                if (e.CancellationToken.IsCancellationRequested)
                                {
                                    e.TrySetCanceled(e.CancellationToken);
                                    continue;
                                }
                                try
                                {
                                    var result = await e.Next();
                                    e.TrySetResult(result);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Mediator auto-batch entry failed for {RequestType}", typeof(TRequest).Name);
                                    var wrapped = new Catga.Exceptions.CatgaException("Auto-batch execution error", ex);
                                    e.TrySetFailure(CatgaResult<TResponse>.Failure("Auto-batch execution error", wrapped));
                                }
                            }
                        }, CancellationToken.None);
                        if (ObservabilityHooks.IsEnabled)
                        {
                            var elapsed = Stopwatch.GetTimestamp() - start;
                            var durationMs = elapsed * 1000.0 / Stopwatch.Frequency;
                            CatgaDiagnostics.MediatorBatchSize.Record(batch.Count);
                            CatgaDiagnostics.MediatorBatchQueueLength.Record(Count);
                            CatgaDiagnostics.MediatorBatchFlushDuration.Record(durationMs);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Mediator auto-batch flush failed for {RequestType}", typeof(TRequest).Name);
                        var wrapped = new Catga.Exceptions.CatgaException("Auto-batch flush error", ex);
                        foreach (var e in batch)
                        {
                            e.TrySetFailure(CatgaResult<TResponse>.Failure("Auto-batch flush error", wrapped));
                        }
                        if (ObservabilityHooks.IsEnabled)
                        {
                            var elapsed = Stopwatch.GetTimestamp() - start;
                            var durationMs = elapsed * 1000.0 / Stopwatch.Frequency;
                            CatgaDiagnostics.MediatorBatchSize.Record(batch.Count);
                            CatgaDiagnostics.MediatorBatchQueueLength.Record(Count);
                            CatgaDiagnostics.MediatorBatchFlushDuration.Record(durationMs);
                        }
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _flushing, 0);
                }
            }
        }

        private readonly struct Entry
        {
            public Entry(TRequest request, PipelineDelegate<TResponse> next, TaskCompletionSource<CatgaResult<TResponse>> tcs, CancellationToken ct)
            {
                Request = request;
                Next = next;
                Tcs = tcs;
                CancellationToken = ct;
            }

            public TRequest Request { get; }
            public PipelineDelegate<TResponse> Next { get; }
            public TaskCompletionSource<CatgaResult<TResponse>> Tcs { get; }
            public CancellationToken CancellationToken { get; }

            public void TrySetResult(CatgaResult<TResponse> result) => Tcs.TrySetResult(result);
            public void TrySetFailure(CatgaResult<TResponse> failure) => Tcs.TrySetResult(failure);
            public void TrySetCanceled(CancellationToken ct) => Tcs.TrySetCanceled(ct);
        }
    }
}
