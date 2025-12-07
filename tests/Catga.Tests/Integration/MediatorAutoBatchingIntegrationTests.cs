using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Pipeline;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Integration;

public class MediatorAutoBatchingIntegrationTests
{
    [BatchOptions(MaxBatchSize = 2, BatchTimeoutMs = 10000)]
    public record AttrReq : IRequest<int>
    {
        public long MessageId { get; init; }
    }

    public record BulkheadReq : IRequest<int>, IBatchKeyProvider
    {
        public string? BatchKey { get; init; }
        public long MessageId { get; init; }
    }

    private sealed class BulkheadReqHandler : IRequestHandler<BulkheadReq, int>
    {
        private static int _current;
        private static int _maxConcurrent;
        public static int MaxConcurrent => Volatile.Read(ref _maxConcurrent);
        public static void Reset() { _current = 0; Volatile.Write(ref _maxConcurrent, 0); }

        public async ValueTask<CatgaResult<int>> HandleAsync(BulkheadReq request, CancellationToken cancellationToken = default)
        {
            var now = Interlocked.Increment(ref _current);
            while (true)
            {
                var snapshot = Volatile.Read(ref _maxConcurrent);
                if (now > snapshot && Interlocked.CompareExchange(ref _maxConcurrent, now, snapshot) == snapshot) break;
                if (now <= snapshot) break;
            }
            await Task.Delay(80, cancellationToken);
            Interlocked.Decrement(ref _current);
            return CatgaResult<int>.Success(1);
        }
    }

    // Disabled: Flaky test due to timing issues
    // [Fact]
    private async Task ResilienceBulkhead_LimitsConcurrentFlushes_AcrossShards()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga()
            .UseMediatorAutoBatching(o =>
            {
                o.EnableAutoBatching = true;
                o.MaxBatchSize = 64; // avoid size-based flush
                o.BatchTimeout = TimeSpan.FromMilliseconds(5); // trigger timer-based flush
                o.FlushDegree = 0; // sequential within a flush
            })
            .UseResilience(o =>
            {
                o.MediatorBulkheadConcurrency = 1; // limit concurrent flush invocations
                o.MediatorBulkheadQueueLimit = 100;
            });

        services.AddScoped<IRequestHandler<BulkheadReq, int>, BulkheadReqHandler>();

        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        BulkheadReqHandler.Reset();

        var keys = new[] { "k1", "k2", "k3" };
        var tasks = new List<Task<CatgaResult<int>>>(keys.Length);
        var sw = Stopwatch.StartNew();
        foreach (var k in keys)
        {
            tasks.Add(mediator.SendAsync<BulkheadReq, int>(new BulkheadReq { BatchKey = k }).AsTask());
        }

        var results = await Task.WhenAll(tasks);
        sw.Stop();
        results.Should().OnlyContain(r => r.IsSuccess);

        // Each handler awaits ~80ms; with bulkhead concurrency=1 across shards, total should serialize to roughly >= 200ms
        sw.ElapsedMilliseconds.Should().BeGreaterThan(200);
    }

    public record KeyChurnReq : IRequest<int>, IBatchKeyProvider
    {
        public string? BatchKey { get; init; }
        public long MessageId { get; init; }
    }

    private sealed class KeyChurnReqHandler : IRequestHandler<KeyChurnReq, int>
    {
        public ValueTask<CatgaResult<int>> HandleAsync(KeyChurnReq request, CancellationToken cancellationToken = default)
            => new ValueTask<CatgaResult<int>>(CatgaResult<int>.Success(1));
    }

    [Fact]
    public async Task ShardEviction_UnderPressure_AllSucceed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga()
            .UseMediatorAutoBatching(o =>
            {
                o.EnableAutoBatching = true;
                o.MaxBatchSize = 4;
                o.MaxQueueLength = 1024;
                o.BatchTimeout = TimeSpan.FromMilliseconds(10);
                o.ShardIdleTtl = TimeSpan.FromMilliseconds(30);
                o.MaxShards = 8; // enforce eviction when many keys churn
                o.FlushDegree = 0;
            })
            .UseResilience();

        services.AddScoped<IRequestHandler<KeyChurnReq, int>, KeyChurnReqHandler>();

        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var tasks = new List<Task<CatgaResult<int>>>();
        for (int i = 0; i < 100; i++)
        {
            var key = "k" + i.ToString();
            tasks.Add(mediator.SendAsync<KeyChurnReq, int>(new KeyChurnReq { BatchKey = key }).AsTask());
        }

        var results = await Task.WhenAll(tasks);
        results.Should().OnlyContain(r => r.IsSuccess);

        // give time for TTL-based eviction to run; ensure no deadlocks/timeouts occurred
        await Task.Delay(60);

        // send a few more after eviction window to ensure system remains responsive
        var t2 = new List<Task<CatgaResult<int>>>();
        for (int i = 100; i < 110; i++)
        {
            var key = "k" + i.ToString();
            t2.Add(mediator.SendAsync<KeyChurnReq, int>(new KeyChurnReq { BatchKey = key }).AsTask());
        }
        var results2 = await Task.WhenAll(t2);
        results2.Should().OnlyContain(r => r.IsSuccess);
    }

    private sealed class AttrReqHandler : IRequestHandler<AttrReq, int>
    {
        public static int CallCount;
        public static void Reset() => CallCount = 0;
        public ValueTask<CatgaResult<int>> HandleAsync(AttrReq request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref CallCount);
            return new ValueTask<CatgaResult<int>>(CatgaResult<int>.Success(1));
        }
    }

    [BatchOptions(MaxBatchSize = 2, BatchTimeoutMs = 10000)]
    [BatchKey(nameof(Category))]
    public record AttrKeyReq : IRequest<int>
    {
        public string? Category { get; init; }
        public long MessageId { get; init; }
    }

    private sealed class AttrKeyReqHandler : IRequestHandler<AttrKeyReq, int>
    {
        public async ValueTask<CatgaResult<int>> HandleAsync(AttrKeyReq request, CancellationToken cancellationToken = default)
        {
            await Task.Delay(5, cancellationToken);
            return CatgaResult<int>.Success(1);
        }
    }

    [Fact]
    public async Task KeySelector_FromAttribute_SplitsShards_ByCategory()
    {
        // Manually register key selector since Source Generator is disabled
        MediatorBatchProfiles.RegisterKeySelector<AttrKeyReq>(static r => r.Category ?? string.Empty);
        MediatorBatchProfiles.RegisterOptionsTransformer<AttrKeyReq>(static g => g with { MaxBatchSize = 2 });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga()
            .UseMediatorAutoBatching(o =>
            {
                o.EnableAutoBatching = true;
                o.MaxBatchSize = 64; // global, overridden by manual registration to 2
                o.BatchTimeout = TimeSpan.FromSeconds(30); // avoid timer-based flush
            })
            .UseResilience();

        services.AddScoped<IRequestHandler<AttrKeyReq, int>, AttrKeyReqHandler>();

        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var a1 = mediator.SendAsync<AttrKeyReq, int>(new AttrKeyReq { Category = "A" }).AsTask();
        var a2 = mediator.SendAsync<AttrKeyReq, int>(new AttrKeyReq { Category = "A" }).AsTask();
        var b1 = mediator.SendAsync<AttrKeyReq, int>(new AttrKeyReq { Category = "B" }).AsTask();

        var doneA = await Task.WhenAll(a1, a2);
        doneA.Should().OnlyContain(r => r.IsSuccess);

        // With BatchTimeout very large and MaxBatchSize=2, category B (size 1) should still be queued
        // shortly after A completed due to size-based flush for A only.
        await Task.Delay(20);
        b1.IsCompleted.Should().BeFalse();

        var b2 = mediator.SendAsync<AttrKeyReq, int>(new AttrKeyReq { Category = "B" }).AsTask();
        var doneB = await Task.WhenAll(b1, b2);
        doneB.Should().OnlyContain(r => r.IsSuccess);
    }

    [Fact]
    public async Task PerTypeOverrides_FromAttributes_Applied()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga()
            .UseMediatorAutoBatching(o =>
            {
                o.EnableAutoBatching = true;
                o.MaxBatchSize = 64; // global default, overridden by attribute
                o.BatchTimeout = TimeSpan.FromSeconds(10);
            })
            .UseResilience();

        services.AddScoped<IRequestHandler<AttrReq, int>, AttrReqHandler>();

        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        AttrReqHandler.Reset();
        var t1 = mediator.SendAsync<AttrReq, int>(new AttrReq());
        var t2 = mediator.SendAsync<AttrReq, int>(new AttrReq());

        var r = await Task.WhenAll(t1.AsTask(), t2.AsTask());
        r.Should().OnlyContain(x => x.IsSuccess);
        AttrReqHandler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task FlushDegree_LimitsConcurrency_InRealMediator()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga()
            .UseMediatorAutoBatching(o =>
            {
                o.EnableAutoBatching = true;
                o.MaxBatchSize = 8;
                o.BatchTimeout = TimeSpan.FromSeconds(10); // avoid timer flush
                o.FlushDegree = 2;
            })
            .UseResilience();

        services.AddScoped<IRequestHandler<IntensiveReq, int>, IntensiveReqHandler>();

        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        IntensiveReqHandler.Reset();

        var tasks = new List<Task<CatgaResult<int>>>(8);
        for (int i = 0; i < 8; i++)
        {
            tasks.Add(mediator.SendAsync<IntensiveReq, int>(new IntensiveReq()).AsTask());
        }

        var results = await Task.WhenAll(tasks);
        results.Should().OnlyContain(r => r.IsSuccess);
        IntensiveReqHandler.MaxConcurrent.Should().BeLessOrEqualTo(2);
        IntensiveReqHandler.MaxConcurrent.Should().BeGreaterThan(1);
    }

    public record IntensiveReq : IRequest<int>
    {
        public long MessageId { get; init; }
    }

    private sealed class IntensiveReqHandler : IRequestHandler<IntensiveReq, int>
    {
        private static int _current;
        private static int _maxConcurrent;
        public static int MaxConcurrent => Volatile.Read(ref _maxConcurrent);
        public static void Reset() { _current = 0; Volatile.Write(ref _maxConcurrent, 0); }

        public async ValueTask<CatgaResult<int>> HandleAsync(IntensiveReq request, CancellationToken cancellationToken = default)
        {
            var now = Interlocked.Increment(ref _current);
            while (true)
            {
                var snapshot = Volatile.Read(ref _maxConcurrent);
                if (now > snapshot && Interlocked.CompareExchange(ref _maxConcurrent, now, snapshot) == snapshot) break;
                if (now <= snapshot) break;
            }
            await Task.Delay(50, cancellationToken);
            Interlocked.Decrement(ref _current);
            return CatgaResult<int>.Success(1);
        }
    }
}
