using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Catga.Abstractions;
using Catga.Core;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Catga.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Pipeline;

public class AutoBatchingBehaviorTests
{
    [Fact]
    public void ProviderTransformer_MergesGlobalAndOverrides()
    {
        var global = new MediatorBatchOptions
        {
            EnableAutoBatching = true,
            MaxBatchSize = 10,
            MaxQueueLength = 1000,
            MaxShards = 128,
        };

        // Simulate SG registration
        MediatorBatchProfiles.RegisterOptionsTransformer<ProviderMergeReq>(static g => g with { MaxBatchSize = 5, MaxShards = 999 });

        var provider = new DefaultMediatorBatchOptionsProvider(global);
        provider.TryGet<ProviderMergeReq>(out var effective).Should().BeTrue();
        effective!.MaxBatchSize.Should().Be(5);
        effective.MaxQueueLength.Should().Be(1000);
        effective.MaxShards.Should().Be(999);
    }

    [Fact]
    public async Task ShardEviction_Pressure_AllRequestsSucceed()
    {
        // Many keys with small MaxShards and short TTL; ensure behavior stays healthy
        MediatorBatchProfiles.RegisterKeySelector<ManyKeysReq>(static r => r.K);

        var logger = Substitute.For<ILogger<AutoBatchingBehavior<ManyKeysReq, int>>>();
        var options = new MediatorBatchOptions
        {
            EnableAutoBatching = true,
            MaxBatchSize = 2,
            BatchTimeout = TimeSpan.FromMilliseconds(15),
            ShardIdleTtl = TimeSpan.FromMilliseconds(30),
            MaxShards = 16
        };
        var provider = new DefaultMediatorBatchOptionsProvider(options);
        var behavior = new AutoBatchingBehavior<ManyKeysReq, int>(logger, options, new NoopResilienceProvider(), provider);

        PipelineDelegate<int> next = () => ValueTask.FromResult(CatgaResult<int>.Success(1));

        // Wave 1: 50 keys, two entries each to flush
        var tasks1 = new List<Task<CatgaResult<int>>>();
        for (int i = 0; i < 50; i++)
        {
            var key = $"K{i}";
            tasks1.Add(behavior.HandleAsync(new ManyKeysReq(key), next).AsTask());
            tasks1.Add(behavior.HandleAsync(new ManyKeysReq(key), next).AsTask());
        }
        var r1 = await Task.WhenAll(tasks1);
        r1.Should().OnlyContain(r => r.IsSuccess);

        // Wait > TTL so idle shards can be evicted by timer
        await Task.Delay(80);

        // Wave 2: another 50 keys
        var tasks2 = new List<Task<CatgaResult<int>>>();
        for (int i = 50; i < 100; i++)
        {
            var key = $"K{i}";
            tasks2.Add(behavior.HandleAsync(new ManyKeysReq(key), next).AsTask());
            tasks2.Add(behavior.HandleAsync(new ManyKeysReq(key), next).AsTask());
        }
        var r2 = await Task.WhenAll(tasks2);
        r2.Should().OnlyContain(r => r.IsSuccess);
    }

    [Fact]
    public async Task KeyedBatching_FlushesPerShard_OnThreshold()
    {
        // Arrange: register key selector via "generator" and create behavior with provider
        MediatorBatchProfiles.RegisterKeySelector<KeyedReq>(static r => r.Key);

        var logger = Substitute.For<ILogger<AutoBatchingBehavior<KeyedReq, int>>>();
        var options = new MediatorBatchOptions
        {
            EnableAutoBatching = true,
            MaxBatchSize = 2,
            BatchTimeout = TimeSpan.FromSeconds(10) // avoid timer, rely on threshold flush
        };
        var provider = new DefaultMediatorBatchOptionsProvider(options);
        var behavior = new AutoBatchingBehavior<KeyedReq, int>(logger, options, new NoopResilienceProvider(), provider);

        int calls = 0;
        PipelineDelegate<int> next = () => { Interlocked.Increment(ref calls); return ValueTask.FromResult(CatgaResult<int>.Success(1)); };

        // Two items for shard A, two for shard B -> expect two separate flushes and 4 handler invocations
        var tasks = new List<Task<CatgaResult<int>>>
        {
            behavior.HandleAsync(new KeyedReq("A"), next).AsTask(),
            behavior.HandleAsync(new KeyedReq("A"), next).AsTask(),
            behavior.HandleAsync(new KeyedReq("B"), next).AsTask(),
            behavior.HandleAsync(new KeyedReq("B"), next).AsTask()
        };

        var results = await Task.WhenAll(tasks);
        results.Should().OnlyContain(r => r.IsSuccess);
        calls.Should().Be(4);
    }

    [Fact]
    public async Task TypedOverride_DisablesBatching_WhenMaxBatchSizeOne()
    {
        // Global enables batching, typed override sets MaxBatchSize=1 -> should bypass batching
        var logger = Substitute.For<ILogger<AutoBatchingBehavior<DisabledReq, int>>>();
        var global = new MediatorBatchOptions { EnableAutoBatching = true, MaxBatchSize = 64, BatchTimeout = TimeSpan.FromSeconds(5) };
        MediatorBatchProfiles.RegisterOptionsTransformer<DisabledReq>(static g => g with { MaxBatchSize = 1 });

        var provider = new DefaultMediatorBatchOptionsProvider(global);
        var behavior = new AutoBatchingBehavior<DisabledReq, int>(logger, global, new NoopResilienceProvider(), provider);

        int calls = 0;
        PipelineDelegate<int> next = () => { Interlocked.Increment(ref calls); return ValueTask.FromResult(CatgaResult<int>.Success(7)); };
        var res = await behavior.HandleAsync(new DisabledReq(), next);
        res.IsSuccess.Should().BeTrue();
        calls.Should().Be(1); // executed directly, not queued
    }

    [Fact]
    public void GeneratedKeySelector_Works_WithoutExtension()
    {
        var services = new ServiceCollection();
        var builder = services.AddCatga();
        builder.UseMediatorAutoBatching(o => o.EnableAutoBatching = true);
        using var sp = services.BuildServiceProvider();

        var provider = sp.GetRequiredService<IMediatorBatchOptionsProvider>();
        provider.TryGetKeySelector<KeyReq>(out var selector).Should().BeTrue();
        selector!(new KeyReq("t-1")).Should().Be("t-1");
    }

    [Fact]
    public void GeneratedKeySelector_Works()
    {
        var services = new ServiceCollection();
        var builder = services.AddCatga();
        builder.UseMediatorAutoBatching(o => o.EnableAutoBatching = true)
               .UseMediatorAutoBatchingProfilesFromAssembly();
        using var sp = services.BuildServiceProvider();

        var provider = sp.GetRequiredService<IMediatorBatchOptionsProvider>();
        provider.TryGetKeySelector<KeyReq>(out var selector).Should().BeTrue();
        selector!(new KeyReq("tenant-42")).Should().Be("tenant-42");
    }

    [Fact]
    public async Task FlushDegree_LimitedConcurrency_Respected()
    {
        var logger = Substitute.For<ILogger<AutoBatchingBehavior<ConcurrencyReq, int>>>();
        var options = new MediatorBatchOptions
        {
            EnableAutoBatching = true,
            MaxBatchSize = 8,
            BatchTimeout = TimeSpan.FromSeconds(5), // avoid timer flush interfering
            FlushDegree = 2
        };
        var provider = new NoopResilienceProvider();
        var behavior = new AutoBatchingBehavior<ConcurrencyReq, int>(logger, options, provider);

        int current = 0, max = 0;
        PipelineDelegate<int> next = async () =>
        {
            var now = Interlocked.Increment(ref current);
            while (true)
            {
                var snapshot = max;
                if (now > snapshot && Interlocked.CompareExchange(ref max, now, snapshot) == snapshot) break;
                if (now <= snapshot) break;
            }
            await Task.Delay(50);
            Interlocked.Decrement(ref current);
            return CatgaResult<int>.Success(1);
        };

        var tasks = new List<Task<CatgaResult<int>>>(8);
        for (int i = 0; i < 8; i++)
        {
            tasks.Add(behavior.HandleAsync(new ConcurrencyReq(), next).AsTask());
        }
        var results = await Task.WhenAll(tasks);
        results.Should().OnlyContain(r => r.IsSuccess);
        max.Should().BeLessOrEqualTo(2);
        max.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task Overflow_ImmediateFailure_WhenQueueLengthZero()
    {
        var logger = Substitute.For<ILogger<AutoBatchingBehavior<OverflowReq, int>>>();
        var options = new MediatorBatchOptions
        {
            EnableAutoBatching = true,
            MaxBatchSize = 1000,
            BatchTimeout = TimeSpan.FromSeconds(5),
            MaxQueueLength = 0
        };
        var provider = new NoopResilienceProvider();
        var behavior = new AutoBatchingBehavior<OverflowReq, int>(logger, options, provider);

        PipelineDelegate<int> next = () => ValueTask.FromResult(CatgaResult<int>.Success(1));
        var result = await behavior.HandleAsync(new OverflowReq(), next);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("overflow");
    }

    // Request types used per test to isolate static aggregator
    public record ProviderMergeReq() : IRequest<int>
    {
        public long MessageId { get; init; }
    }

    [BatchKey(nameof(TenantId))]
    public record KeyReq(string TenantId) : IRequest<int>
    {
        public long MessageId { get; init; }
    }

    public record ConcurrencyReq() : IRequest<int>
    {
        public long MessageId { get; init; }
    }
    public record OverflowReq() : IRequest<int>
    {
        public long MessageId { get; init; }
    }

    public record DisabledReq() : IRequest<int>
    {
        public long MessageId { get; init; }
    }

    public record KeyedReq(string Key) : IRequest<int>
    {
        public long MessageId { get; init; }
    }

    public record ManyKeysReq(string K) : IRequest<int>
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
