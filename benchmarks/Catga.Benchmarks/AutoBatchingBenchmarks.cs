using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 5)]
public class AutoBatchingBenchmarks
{
    private IServiceProvider _spUnkeyedSeq_NoRes = null!;
    private IServiceProvider _spUnkeyedDeg2_NoRes = null!;
    private IServiceProvider _spKeyedSeq_NoRes = null!;
    private IServiceProvider _spUnkeyedSeq_WithRes = null!;

    private ICatgaMediator _medUnkeyedSeq_NoRes = null!;
    private ICatgaMediator _medUnkeyedDeg2_NoRes = null!;
    private ICatgaMediator _medKeyedSeq_NoRes = null!;
    private ICatgaMediator _medUnkeyedSeq_WithRes = null!;

    [GlobalSetup]
    public void Setup()
    {
        _spUnkeyedSeq_NoRes = BuildProvider(enableResilience: false, flushDegree: 0, keyed: false);
        _spUnkeyedDeg2_NoRes = BuildProvider(enableResilience: false, flushDegree: 2, keyed: false);
        _spKeyedSeq_NoRes = BuildProvider(enableResilience: false, flushDegree: 0, keyed: true);
        _spUnkeyedSeq_WithRes = BuildProvider(enableResilience: true, flushDegree: 0, keyed: false);

        _medUnkeyedSeq_NoRes = _spUnkeyedSeq_NoRes.GetRequiredService<ICatgaMediator>();
        _medUnkeyedDeg2_NoRes = _spUnkeyedDeg2_NoRes.GetRequiredService<ICatgaMediator>();
        _medKeyedSeq_NoRes = _spKeyedSeq_NoRes.GetRequiredService<ICatgaMediator>();
        _medUnkeyedSeq_WithRes = _spUnkeyedSeq_WithRes.GetRequiredService<ICatgaMediator>();
    }

internal sealed class NoopResiliencePipelineProvider : Catga.Resilience.IResiliencePipelineProvider
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

    [Benchmark(Baseline = true, Description = "Unkeyed Sequential (FlushDegree=0) - No Resilience")]
    public async Task Unkeyed_Sequential_NoResilience()
        => await RunBatchAsync(_medUnkeyedSeq_NoRes, keyed: false);

    [Benchmark(Description = "Unkeyed Limited (FlushDegree=2) - No Resilience")]
    public async Task Unkeyed_FlushDegree2_NoResilience()
        => await RunBatchAsync(_medUnkeyedDeg2_NoRes, keyed: false);

    [Benchmark(Description = "Keyed Sequential (4 shards) - No Resilience")]
    public async Task Keyed_Sequential_NoResilience()
        => await RunBatchAsync(_medKeyedSeq_NoRes, keyed: true);

    [Benchmark(Description = "Unkeyed Sequential - With Resilience")]
    public async Task Unkeyed_Sequential_WithResilience()
        => await RunBatchAsync(_medUnkeyedSeq_WithRes, keyed: false);

    private static IServiceProvider BuildProvider(bool enableResilience, int flushDegree, bool keyed)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddCatga()
            .UseMediatorAutoBatching(o =>
            {
                o.EnableAutoBatching = true;
                o.MaxBatchSize = 16;
                o.MaxQueueLength = 10_000;
                o.BatchTimeout = TimeSpan.FromSeconds(10); // avoid timer flush
                o.FlushDegree = flushDegree;
            });
        if (enableResilience)
        {
            builder.UseResilience(o =>
            {
                o.MediatorBulkheadConcurrency = Math.Max(Environment.ProcessorCount, 4);
                o.MediatorBulkheadQueueLimit = o.MediatorBulkheadConcurrency * 2;
                o.MediatorTimeout = TimeSpan.FromSeconds(5);
            });
        }
        else
        {
            // Override the TryAdd fallback with a Noop provider so flush path has almost no wrapper overhead
            services.AddSingleton<Catga.Resilience.IResiliencePipelineProvider, NoopResiliencePipelineProvider>();
        }

        if (keyed)
        {
            services.AddScoped<IRequestHandler<KeyedReq, int>, KeyedReqHandler>();
        }
        else
        {
            services.AddScoped<IRequestHandler<UnkeyedReq, int>, UnkeyedReqHandler>();
        }

        return services.BuildServiceProvider();
    }

    private static async Task RunBatchAsync(ICatgaMediator mediator, bool keyed)
    {
        const int n = 16; // match MaxBatchSize so size-based flush triggers
        if (!keyed)
        {
            var tasks = new List<Task<CatgaResult<int>>>(n);
            for (int i = 0; i < n; i++)
                tasks.Add(mediator.SendAsync<UnkeyedReq, int>(new UnkeyedReq()).AsTask());
            var results = await Task.WhenAll(tasks);
            foreach (var r in results) if (!r.IsSuccess) throw new Exception("Failure");
        }
        else
        {
            // Use 4 shards; send 16 per shard => 64 total to trigger size flush in each
            var keys = new[] { "A", "B", "C", "D" };
            var tasks = new List<Task<CatgaResult<int>>>(n * keys.Length);
            foreach (var k in keys)
            {
                for (int i = 0; i < n; i++)
                    tasks.Add(mediator.SendAsync<KeyedReq, int>(new KeyedReq { BatchKey = k }).AsTask());
            }
            var results = await Task.WhenAll(tasks);
            foreach (var r in results) if (!r.IsSuccess) throw new Exception("Failure");
        }
    }

    [BatchOptions(MaxBatchSize = 16, BatchTimeoutMs = 10_000)]
    public record UnkeyedReq : IRequest<int>
    {
        public long MessageId { get; init; }
    }

    private sealed class UnkeyedReqHandler : IRequestHandler<UnkeyedReq, int>
    {
        public Task<CatgaResult<int>> HandleAsync(UnkeyedReq request, CancellationToken cancellationToken = default)
            => Task.FromResult(CatgaResult<int>.Success(1));
    }

    [BatchOptions(MaxBatchSize = 16, BatchTimeoutMs = 10_000)]
    public record KeyedReq : IRequest<int>, IBatchKeyProvider
    {
        public string? BatchKey { get; init; }
        public long MessageId { get; init; }
    }

    private sealed class KeyedReqHandler : IRequestHandler<KeyedReq, int>
    {
        public Task<CatgaResult<int>> HandleAsync(KeyedReq request, CancellationToken cancellationToken = default)
            => Task.FromResult(CatgaResult<int>.Success(1));
    }
}
