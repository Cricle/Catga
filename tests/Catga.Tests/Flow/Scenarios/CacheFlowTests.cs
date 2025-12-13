using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Cache flow scenario tests.
/// Tests caching patterns, cache invalidation, and cache-aside strategies.
/// </summary>
public class CacheFlowTests
{
    private IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IMessageSerializer, TestSerializer>();
        services.AddSingleton<IDslFlowStore, Catga.Persistence.InMemory.Flow.InMemoryDslFlowStore>();
        services.AddSingleton<IDslFlowExecutor, DslFlowExecutor>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Cache_Hit_ReturnsFromCache()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var cache = new SimpleCache();
        var dbCalls = 0;

        cache.Set("product-001", "Cached Product Data");

        var flow = FlowBuilder.Create<CacheState>("cache-hit")
            .Step("get-data", async (state, ct) =>
            {
                var cached = cache.Get(state.Key);
                if (cached != null)
                {
                    state.Data = cached;
                    state.FromCache = true;
                    return true;
                }

                // Cache miss - go to DB
                dbCalls++;
                state.Data = $"DB Data for {state.Key}";
                state.FromCache = false;
                cache.Set(state.Key, state.Data);
                return true;
            })
            .Build();

        var state = new CacheState { FlowId = "cache-test", Key = "product-001" };

        var result = await executor.ExecuteAsync(flow, state);

        result.State.FromCache.Should().BeTrue();
        result.State.Data.Should().Be("Cached Product Data");
        dbCalls.Should().Be(0);
    }

    [Fact]
    public async Task Cache_Miss_FetchesFromSource()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var cache = new SimpleCache();
        var dbCalls = 0;

        var flow = FlowBuilder.Create<CacheState>("cache-miss")
            .Step("get-data", async (state, ct) =>
            {
                var cached = cache.Get(state.Key);
                if (cached != null)
                {
                    state.Data = cached;
                    state.FromCache = true;
                    return true;
                }

                dbCalls++;
                state.Data = $"DB Data for {state.Key}";
                state.FromCache = false;
                cache.Set(state.Key, state.Data);
                return true;
            })
            .Build();

        var state = new CacheState { FlowId = "miss-test", Key = "product-new" };

        var result = await executor.ExecuteAsync(flow, state);

        result.State.FromCache.Should().BeFalse();
        dbCalls.Should().Be(1);
        cache.Get("product-new").Should().NotBeNull(); // Now cached
    }

    [Fact]
    public async Task Cache_Expiry_RefetchesAfterTTL()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var cache = new ExpiringCache();
        var fetchCount = 0;

        var flow = FlowBuilder.Create<CacheState>("cache-expiry")
            .Step("get-data", async (state, ct) =>
            {
                var cached = cache.Get(state.Key);
                if (cached != null)
                {
                    state.Data = cached;
                    state.FromCache = true;
                    return true;
                }

                fetchCount++;
                state.Data = $"Fetched-{fetchCount}";
                state.FromCache = false;
                cache.Set(state.Key, state.Data, TimeSpan.FromMilliseconds(50));
                return true;
            })
            .Build();

        // First fetch
        await executor.ExecuteAsync(flow, new CacheState { FlowId = "exp-1", Key = "expiring-key" });

        // Second fetch - should hit cache
        var result2 = await executor.ExecuteAsync(flow, new CacheState { FlowId = "exp-2", Key = "expiring-key" });
        result2.State.FromCache.Should().BeTrue();

        // Wait for expiry
        await Task.Delay(100);

        // Third fetch - should miss cache
        var result3 = await executor.ExecuteAsync(flow, new CacheState { FlowId = "exp-3", Key = "expiring-key" });
        result3.State.FromCache.Should().BeFalse();

        fetchCount.Should().Be(2);
    }

    [Fact]
    public async Task Cache_Invalidation_ForcesRefresh()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var cache = new SimpleCache();

        cache.Set("user-001", "Old User Data");

        var flow = FlowBuilder.Create<InvalidationState>("cache-invalidation")
            .Step("update-data", async (state, ct) =>
            {
                // Update the source
                state.UpdatedData = state.NewData;

                // Invalidate cache
                if (state.InvalidateCache)
                {
                    cache.Remove(state.Key);
                    state.CacheInvalidated = true;
                }

                return true;
            })
            .Build();

        var state = new InvalidationState
        {
            FlowId = "invalidate-test",
            Key = "user-001",
            NewData = "New User Data",
            InvalidateCache = true
        };

        await executor.ExecuteAsync(flow, state);

        cache.Get("user-001").Should().BeNull();
    }

    [Fact]
    public async Task Cache_WriteThrough_UpdatesBothCacheAndSource()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var cache = new SimpleCache();
        var database = new ConcurrentDictionary<string, string>();

        var flow = FlowBuilder.Create<WriteThroughState>("write-through")
            .Step("write", async (state, ct) =>
            {
                // Write to source first
                database[state.Key] = state.Data;
                state.WrittenToSource = true;

                // Then update cache
                cache.Set(state.Key, state.Data);
                state.WrittenToCache = true;

                return true;
            })
            .Build();

        var state = new WriteThroughState
        {
            FlowId = "write-through-test",
            Key = "item-001",
            Data = "New Item Data"
        };

        await executor.ExecuteAsync(flow, state);

        database["item-001"].Should().Be("New Item Data");
        cache.Get("item-001").Should().Be("New Item Data");
    }

    [Fact]
    public async Task Cache_WriteBehind_UpdatesCacheImmediately()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var cache = new SimpleCache();
        var pendingWrites = new ConcurrentQueue<(string Key, string Value)>();

        var flow = FlowBuilder.Create<WriteBehindState>("write-behind")
            .Step("write", async (state, ct) =>
            {
                // Update cache immediately
                cache.Set(state.Key, state.Data);
                state.CacheUpdated = true;

                // Queue write to source (async)
                pendingWrites.Enqueue((state.Key, state.Data));
                state.WriteQueued = true;

                return true;
            })
            .Build();

        var state = new WriteBehindState
        {
            FlowId = "write-behind-test",
            Key = "async-item",
            Data = "Async Data"
        };

        await executor.ExecuteAsync(flow, state);

        state.CacheUpdated.Should().BeTrue();
        state.WriteQueued.Should().BeTrue();
        cache.Get("async-item").Should().Be("Async Data");
        pendingWrites.Should().HaveCount(1);
    }

    [Fact]
    public async Task Cache_MultiLevel_ChecksAllLevels()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var l1Cache = new SimpleCache(); // Fast, small
        var l2Cache = new SimpleCache(); // Slower, larger
        var sourceCalls = 0;

        l2Cache.Set("multi-key", "L2 Data");

        var flow = FlowBuilder.Create<MultiLevelCacheState>("multi-level")
            .Step("get-data", async (state, ct) =>
            {
                // Check L1
                var l1Data = l1Cache.Get(state.Key);
                if (l1Data != null)
                {
                    state.Data = l1Data;
                    state.Source = "L1";
                    return true;
                }

                // Check L2
                var l2Data = l2Cache.Get(state.Key);
                if (l2Data != null)
                {
                    state.Data = l2Data;
                    state.Source = "L2";
                    l1Cache.Set(state.Key, l2Data); // Populate L1
                    return true;
                }

                // Go to source
                sourceCalls++;
                state.Data = "Source Data";
                state.Source = "Database";
                l1Cache.Set(state.Key, state.Data);
                l2Cache.Set(state.Key, state.Data);
                return true;
            })
            .Build();

        // First call - L2 hit
        var result1 = await executor.ExecuteAsync(flow, new MultiLevelCacheState { FlowId = "ml-1", Key = "multi-key" });
        result1.State.Source.Should().Be("L2");

        // Second call - L1 hit (populated from L2)
        var result2 = await executor.ExecuteAsync(flow, new MultiLevelCacheState { FlowId = "ml-2", Key = "multi-key" });
        result2.State.Source.Should().Be("L1");

        sourceCalls.Should().Be(0);
    }

    [Fact]
    public async Task Cache_Stampede_Prevention_SingleFetch()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var cache = new SimpleCache();
        var fetchCount = 0;
        var fetchLock = new SemaphoreSlim(1);

        var flow = FlowBuilder.Create<CacheState>("stampede-prevention")
            .Step("get-data", async (state, ct) =>
            {
                var cached = cache.Get(state.Key);
                if (cached != null)
                {
                    state.Data = cached;
                    state.FromCache = true;
                    return true;
                }

                // Prevent stampede with lock
                await fetchLock.WaitAsync(ct);
                try
                {
                    // Double-check after acquiring lock
                    cached = cache.Get(state.Key);
                    if (cached != null)
                    {
                        state.Data = cached;
                        state.FromCache = true;
                        return true;
                    }

                    // Fetch from source
                    Interlocked.Increment(ref fetchCount);
                    await Task.Delay(50, ct); // Simulate slow fetch
                    state.Data = "Fetched Data";
                    state.FromCache = false;
                    cache.Set(state.Key, state.Data);
                }
                finally
                {
                    fetchLock.Release();
                }
                return true;
            })
            .Build();

        // Concurrent requests for same key
        var tasks = Enumerable.Range(1, 10).Select(i =>
            executor.ExecuteAsync(flow, new CacheState { FlowId = $"stampede-{i}", Key = "hot-key" }).AsTask()
        );

        await Task.WhenAll(tasks);

        // Only one fetch should happen
        fetchCount.Should().Be(1);
    }

    [Fact]
    public async Task Cache_SoftDelete_MarksAsDeleted()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var cache = new SoftDeleteCache();

        cache.Set("soft-key", "Original Data");

        var flow = FlowBuilder.Create<SoftDeleteState>("soft-delete")
            .Step("delete", async (state, ct) =>
            {
                cache.SoftDelete(state.Key);
                state.Deleted = true;
                return true;
            })
            .Build();

        await executor.ExecuteAsync(flow, new SoftDeleteState { FlowId = "soft-del", Key = "soft-key" });

        cache.Get("soft-key").Should().BeNull();
        cache.IsDeleted("soft-key").Should().BeTrue();
    }

    #region State Classes and Cache Implementations

    public class CacheState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string Key { get; set; } = "";
        public string? Data { get; set; }
        public bool FromCache { get; set; }
    }

    public class InvalidationState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string Key { get; set; } = "";
        public string? NewData { get; set; }
        public string? UpdatedData { get; set; }
        public bool InvalidateCache { get; set; }
        public bool CacheInvalidated { get; set; }
    }

    public class WriteThroughState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string Key { get; set; } = "";
        public string Data { get; set; } = "";
        public bool WrittenToSource { get; set; }
        public bool WrittenToCache { get; set; }
    }

    public class WriteBehindState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string Key { get; set; } = "";
        public string Data { get; set; } = "";
        public bool CacheUpdated { get; set; }
        public bool WriteQueued { get; set; }
    }

    public class MultiLevelCacheState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string Key { get; set; } = "";
        public string? Data { get; set; }
        public string? Source { get; set; }
    }

    public class SoftDeleteState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string Key { get; set; } = "";
        public bool Deleted { get; set; }
    }

    public class SimpleCache
    {
        private readonly ConcurrentDictionary<string, string> _cache = new();

        public void Set(string key, string value) => _cache[key] = value;
        public string? Get(string key) => _cache.TryGetValue(key, out var v) ? v : null;
        public void Remove(string key) => _cache.TryRemove(key, out _);
    }

    public class ExpiringCache
    {
        private readonly ConcurrentDictionary<string, (string Value, DateTime Expiry)> _cache = new();

        public void Set(string key, string value, TimeSpan ttl)
        {
            _cache[key] = (value, DateTime.UtcNow.Add(ttl));
        }

        public string? Get(string key)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.Expiry > DateTime.UtcNow)
                    return entry.Value;
                _cache.TryRemove(key, out _);
            }
            return null;
        }
    }

    public class SoftDeleteCache
    {
        private readonly ConcurrentDictionary<string, string> _cache = new();
        private readonly ConcurrentDictionary<string, bool> _deleted = new();

        public void Set(string key, string value) => _cache[key] = value;

        public string? Get(string key)
        {
            if (_deleted.ContainsKey(key)) return null;
            return _cache.TryGetValue(key, out var v) ? v : null;
        }

        public void SoftDelete(string key) => _deleted[key] = true;
        public bool IsDeleted(string key) => _deleted.ContainsKey(key);
    }

    #endregion

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
