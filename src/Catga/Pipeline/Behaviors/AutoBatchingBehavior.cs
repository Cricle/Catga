using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
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
    private readonly IMediatorBatchOptionsProvider? _optionsProvider;

    public AutoBatchingBehavior(ILogger<AutoBatchingBehavior<TRequest, TResponse>> logger, MediatorBatchOptions options, IResiliencePipelineProvider provider, IHostApplicationLifetime appLifetime)
        : base(logger)
    {
        _options = options ?? new MediatorBatchOptions();
        _provider = provider;
        EnsureAggregatorInitialized(_options, _provider, Logger, appLifetime.ApplicationStopping, _optionsProvider);
    }

    // Fallback for environments without IHostApplicationLifetime (unit tests, simple ServiceCollection)
    public AutoBatchingBehavior(ILogger<AutoBatchingBehavior<TRequest, TResponse>> logger, MediatorBatchOptions options, IResiliencePipelineProvider provider)
        : base(logger)
    {
        _options = options ?? new MediatorBatchOptions();
        _provider = provider;
        EnsureAggregatorInitialized(_options, _provider, Logger, CancellationToken.None, _optionsProvider);
    }

    // Preferred constructor when IMediatorBatchOptionsProvider is registered via DI
    public AutoBatchingBehavior(ILogger<AutoBatchingBehavior<TRequest, TResponse>> logger, MediatorBatchOptions options, IResiliencePipelineProvider provider, IHostApplicationLifetime appLifetime, IMediatorBatchOptionsProvider optionsProvider)
        : base(logger)
    {
        _options = options ?? new MediatorBatchOptions();
        _provider = provider;
        _optionsProvider = optionsProvider;
        if (_options.EnableAutoBatching)
        {
            EnsureAggregatorInitialized(_options, _provider, Logger, appLifetime.ApplicationStopping, _optionsProvider);
        }
    }

    // Preferred constructor without IHostApplicationLifetime
    public AutoBatchingBehavior(ILogger<AutoBatchingBehavior<TRequest, TResponse>> logger, MediatorBatchOptions options, IResiliencePipelineProvider provider, IMediatorBatchOptionsProvider optionsProvider)
        : base(logger)
    {
        _options = options ?? new MediatorBatchOptions();
        _provider = provider;
        _optionsProvider = optionsProvider;
        if (_options.EnableAutoBatching)
        {
            EnsureAggregatorInitialized(_options, _provider, Logger, CancellationToken.None, _optionsProvider);
        }
    }

    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        // Determine effective options: per-type overrides via provider, fallback to global
        var effective = _optionsProvider != null ? _optionsProvider.GetEffective<TRequest>(_options) : _options;

        if (!effective.EnableAutoBatching || effective.MaxBatchSize <= 1)
        {
            return await next();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            var tcs = new TaskCompletionSource<CatgaResult<TResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
            tcs.SetCanceled(cancellationToken);
            return await tcs.Task;
        }

        var aggregator = _aggregator; // may be null if disabled for this type
        if (aggregator is null)
        {
            return await next();
        }
        return await aggregator.EnqueueAsync(request, next, cancellationToken);
    }

    private static void EnsureAggregatorInitialized(MediatorBatchOptions options, IResiliencePipelineProvider provider, ILogger logger, CancellationToken stop, IMediatorBatchOptionsProvider? optionsProvider)
    {
        if (_aggregator != null) return;
        lock (_initLock)
        {
            if (_aggregator == null)
            {
                var effective = optionsProvider != null ? optionsProvider.GetEffective<TRequest>(options) : options;
                var selector = optionsProvider != null ? optionsProvider.GetKeySelectorOrDefault<TRequest>() : null;
                if (effective.EnableAutoBatching && effective.MaxBatchSize > 1)
                {
                    _aggregator = new Aggregator(effective, provider, logger, stop, selector);
                }
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
        private readonly TimeSpan _period;
        private int _timerActive;
        private readonly CancellationToken _stop;
        private readonly CancellationTokenRegistration _stopReg;
        private readonly Func<TRequest, string?>? _keySelector;

        public Aggregator(MediatorBatchOptions options, IResiliencePipelineProvider provider, ILogger logger, CancellationToken stop, Func<TRequest, string?>? keySelector)
        {
            _options = options;
            _provider = provider;
            _logger = logger;
            _stop = stop;
            _keySelector = keySelector;
            var ms = Math.Max(1.0, options.BatchTimeout.TotalMilliseconds);
            var jitter = 1.0 + (Random.Shared.NextDouble() * 0.2 - 0.1);
            _period = TimeSpan.FromMilliseconds(Math.Max(1, ms * jitter));
            _timer = new Timer(OnTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _timerActive = 0;
            _stopReg = _stop.Register(static s => ((Timer)s!).Dispose(), _timer);
        }

        public ValueTask<CatgaResult<TResponse>> EnqueueAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<CatgaResult<TResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entry = new Entry(request, next, tcs, ct);

            string key;
            if (_keySelector != null)
            {
                key = _keySelector(request) ?? "_";
            }
            else if (request is Catga.Abstractions.IBatchKeyProvider kp && !string.IsNullOrEmpty(kp.BatchKey))
            {
                key = kp.BatchKey!;
            }
            else
            {
                key = "_";
            }
            var shard = _shards.GetOrAdd(key, k => new Shard(k, _options, _provider, _logger));
            var newCount = shard.Enqueue(entry);
            if (newCount >= _options.MaxBatchSize) _ = Task.Run(shard.FlushAsync);
            else EnsureTimerActive();

            return new ValueTask<CatgaResult<TResponse>>(tcs.Task);
        }

        private void EnsureTimerActive()
        {
            if (Volatile.Read(ref _timerActive) == 1) return;
            if (Interlocked.Exchange(ref _timerActive, 1) == 0)
            {
                try { _timer.Change(_period, _period); }
                catch { /* ignore disposal races */ }
            }
        }

        private void OnTimer(object? state)
        {
            try
            {
                if (_stop.IsCancellationRequested) return;
                // Cleanup idle shards by TTL and enforce max shard limit
                var nowTicks = DateTime.UtcNow.Ticks;
                foreach (var kv in _shards)
                {
                    var shard = kv.Value;
                    if (shard.Count > 0)
                    {
                        _ = Task.Run(shard.FlushAsync);
                    }
                    else
                    {
                        if (nowTicks - shard.LastSeenTicks >= _options.ShardIdleTtl.Ticks)
                        {
                            _shards.TryRemove(kv.Key, out _);
                        }
                    }
                }

                var limit = _options.MaxShards;
                if (limit > 0)
                {
                    var current = _shards.Count;
                    if (current > limit)
                    {
                        var over = current - limit;
                        if (over > 0)
                        {
                            var idle = new List<KeyValuePair<string, long>>();
                            foreach (var kv in _shards)
                            {
                                var s = kv.Value;
                                if (s.Count == 0)
                                {
                                    idle.Add(new KeyValuePair<string, long>(kv.Key, s.LastSeenTicks));
                                }
                            }
                            if (idle.Count > 0)
                            {
                                foreach (var candidate in idle.OrderBy(p => p.Value))
                                {
                                    if (over == 0) break;
                                    if (_shards.TryRemove(candidate.Key, out _)) over--;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CatgaLog.MediatorBatchTimerError(_logger, ex, typeof(TRequest).Name);
            }
            finally
            {
                if (_shards.IsEmpty)
                {
                    try { _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan); }
                    catch { /* ignore disposal races */ }
                    Interlocked.Exchange(ref _timerActive, 0);
                }
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
            private long _lastSeenTicks;

            public Shard(string key, MediatorBatchOptions options, IResiliencePipelineProvider provider, ILogger logger)
            {
                _key = key;
                _options = options;
                _provider = provider;
                _logger = logger;
                _lastSeenTicks = DateTime.UtcNow.Ticks;
            }

            public int Count => Volatile.Read(ref _count);
            public long LastSeenTicks => Volatile.Read(ref _lastSeenTicks);

            public int Enqueue(Entry entry)
            {
                _queue.Enqueue(entry);
                var newCount = Interlocked.Increment(ref _count);
                Volatile.Write(ref _lastSeenTicks, DateTime.UtcNow.Ticks);
                
                // Backpressure: drop oldest when exceeding MaxQueueLength
                if (newCount > _options.MaxQueueLength)
                {
                    // Try to drop oldest items until we're back under the limit
                    while (_count > _options.MaxQueueLength && _queue.TryDequeue(out var dropped))
                    {
                        Interlocked.Decrement(ref _count);
                        dropped.TrySetFailure(CatgaResult<TResponse>.Failure("Mediator batch queue overflow"));
                        if (ObservabilityHooks.IsEnabled) ObservabilityHooks.RecordMediatorBatchOverflow();
                        CatgaLog.MediatorBatchOverflow(_logger, typeof(TRequest).Name);
                        System.Diagnostics.Activity.Current?.AddActivityEvent("Mediator.Batch.Overflow",
                            ("request.type", typeof(TRequest).Name),
                            ("key", _key));
                    }
                }
                return newCount;
            }

            public async Task FlushAsync()
            {
                if (Interlocked.Exchange(ref _flushing, 1) == 1) return;
                try
                {
                    using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Mediator.Batch.Flush", ActivityKind.Internal);
                    var batch = new List<Entry>(_options.MaxBatchSize);
                    while (batch.Count < _options.MaxBatchSize && _queue.TryDequeue(out var item))
                    {
                        Interlocked.Decrement(ref _count);
                        batch.Add(item);
                    }
                    if (batch.Count == 0) return;
                    var start = Stopwatch.GetTimestamp();
                    Volatile.Write(ref _lastSeenTicks, DateTime.UtcNow.Ticks);
                    activity?.AddActivityEvent("Mediator.Batch.Collected",
                        ("count", batch.Count));
                    try
                    {
                        await _provider.ExecuteMediatorAsync(async ct =>
                        {
                            async Task ExecuteEntryCoreAsync(Entry e)
                            {
                                if (e.CancellationToken.IsCancellationRequested)
                                {
                                    e.TrySetCanceled(e.CancellationToken);
                                    return;
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

                            if (_options.FlushDegree <= 0)
                            {
                                foreach (var e in batch)
                                {
                                    await ExecuteEntryCoreAsync(e);
                                }
                            }
                            else
                            {
                                var degree = Math.Max(1, _options.FlushDegree);
                                using var semaphore = new SemaphoreSlim(degree, degree);
                                var tasks = new List<Task>(batch.Count);

                                foreach (var e in batch)
                                {
                                    tasks.Add(RunWithSemaphoreAsync(e));
                                }

                                await Task.WhenAll(tasks);

                                async Task RunWithSemaphoreAsync(Entry e)
                                {
                                    await semaphore.WaitAsync(CancellationToken.None);
                                    try
                                    {
                                        await ExecuteEntryCoreAsync(e);
                                    }
                                    finally
                                    {
                                        semaphore.Release();
                                    }
                                }
                            }
                        }, CancellationToken.None);
                        if (ObservabilityHooks.IsEnabled)
                        {
                            var elapsed = Stopwatch.GetTimestamp() - start;
                            var durationMs = elapsed * 1000.0 / Stopwatch.Frequency;
                            ObservabilityHooks.RecordMediatorBatchMetrics(batch.Count, Count, durationMs);
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
                        activity?.SetError(ex);
                        if (ObservabilityHooks.IsEnabled)
                        {
                            var elapsed = Stopwatch.GetTimestamp() - start;
                            var durationMs = elapsed * 1000.0 / Stopwatch.Frequency;
                            ObservabilityHooks.RecordMediatorBatchMetrics(batch.Count, Count, durationMs);
                        }
                    }
                    finally
                    {
                        var elapsed = Stopwatch.GetTimestamp() - start;
                        var durationMs = elapsed * 1000.0 / Stopwatch.Frequency;
                        activity?.AddActivityEvent("Mediator.Batch.Done",
                            ("count", batch.Count),
                            ("duration.ms", durationMs));
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
