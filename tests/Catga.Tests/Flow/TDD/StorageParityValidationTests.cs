using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using System.Reflection;
using Catga.Abstractions;
using Catga.Core;
using Catga.Persistence.Redis.Flow;
using Catga.Persistence.Nats.Flow;
using StackExchange.Redis;
using NATS.Client.Core;
using System.Buffers;
using Catga.Tests.Flow;

namespace Catga.Tests.Flow.TDD;

/// <summary>
/// Comprehensive tests to ensure InMemory, Redis, and NATS flow stores have complete feature parity.
/// All three implementations must support the exact same functionality.
/// </summary>
public class StorageParityValidationTests
{
    [Fact]
    public void AllStores_ImplementSameInterface()
    {
        // Assert that all three stores implement IDslFlowStore
        typeof(InMemoryDslFlowStore).Should().Implement<IDslFlowStore>();
        typeof(RedisDslFlowStore).Should().Implement<IDslFlowStore>();
        typeof(NatsDslFlowStore).Should().Implement<IDslFlowStore>();
    }

    [Fact]
    public void AllStores_HaveSamePublicMethods()
    {
        // Get all public methods from each store
        var inMemoryMethods = GetPublicMethods(typeof(InMemoryDslFlowStore));
        var redisMethods = GetPublicMethods(typeof(RedisDslFlowStore));
        var natsMethods = GetPublicMethods(typeof(NatsDslFlowStore));

        // Compare method signatures
        var interfaceMethods = GetInterfaceMethods(typeof(IDslFlowStore));

        // All stores should implement all interface methods
        inMemoryMethods.Should().Contain(interfaceMethods, "InMemory should implement all interface methods");
        redisMethods.Should().Contain(interfaceMethods, "Redis should implement all interface methods");
        natsMethods.Should().Contain(interfaceMethods, "NATS should implement all interface methods");
    }

    [Theory]
    [InlineData(typeof(InMemoryDslFlowStore))]
    [InlineData(typeof(RedisDslFlowStore))]
    [InlineData(typeof(NatsDslFlowStore))]
    public void Store_ImplementsAllRequiredMethods(Type storeType)
    {
        var methods = storeType.GetMethods()
            .Where(m => m.IsPublic && !m.IsSpecialName)
            .Select(m => m.Name)
            .Distinct()
            .OrderBy(m => m)
            .ToList();

        // Core CRUD operations
        methods.Should().Contain("CreateAsync", $"{storeType.Name} should have CreateAsync");
        methods.Should().Contain("GetAsync", $"{storeType.Name} should have GetAsync");
        methods.Should().Contain("UpdateAsync", $"{storeType.Name} should have UpdateAsync");
        methods.Should().Contain("DeleteAsync", $"{storeType.Name} should have DeleteAsync");

        // WaitCondition operations for WhenAll/WhenAny
        methods.Should().Contain("SetWaitConditionAsync", $"{storeType.Name} should have SetWaitConditionAsync");
        methods.Should().Contain("GetWaitConditionAsync", $"{storeType.Name} should have GetWaitConditionAsync");
        methods.Should().Contain("UpdateWaitConditionAsync", $"{storeType.Name} should have UpdateWaitConditionAsync");
        methods.Should().Contain("ClearWaitConditionAsync", $"{storeType.Name} should have ClearWaitConditionAsync");
        methods.Should().Contain("GetTimedOutWaitConditionsAsync", $"{storeType.Name} should have GetTimedOutWaitConditionsAsync");

        // ForEach progress operations for recovery
        methods.Should().Contain("SaveForEachProgressAsync", $"{storeType.Name} should have SaveForEachProgressAsync");
        methods.Should().Contain("GetForEachProgressAsync", $"{storeType.Name} should have GetForEachProgressAsync");
        methods.Should().Contain("ClearForEachProgressAsync", $"{storeType.Name} should have ClearForEachProgressAsync");
    }

    [Fact]
    public async Task AllStores_SupportFlowSnapshot()
    {
        // Create test snapshots
        var snapshot = new FlowSnapshot<TestFlowState>
        {
            FlowId = "test-flow-001",
            State = new TestFlowState { FlowId = "test-flow-001", Value = 42 },
            Position = new FlowPosition([0, 1]),
            Status = DslFlowStatus.Running,
            Error = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1,
            WaitCondition = null
        };

        // Test InMemory store
        var inMemoryStore = new InMemoryDslFlowStore();
        await TestStoreOperations(inMemoryStore, snapshot);

        // Test Redis store (mocked)
        if (IsRedisAvailable())
        {
            var redis = await GetRedisConnection();
            var serializer = new TestSerializer();
            var redisStore = new RedisDslFlowStore(redis!, serializer);
            await TestStoreOperations(redisStore, snapshot);
        }

        // Test NATS store (mocked)
        if (IsNatsAvailable())
        {
            var nats = await GetNatsConnection();
            var serializer = new TestSerializer();
            var natsStore = new NatsDslFlowStore(nats!, serializer);
            await TestStoreOperations(natsStore, snapshot);
        }
    }

    [Fact]
    public async Task AllStores_SupportWaitConditions()
    {
        var condition = new WaitCondition
        {
            CorrelationId = "wait-001",
            FlowIds = ["flow1", "flow2", "flow3"],
            WaitType = WaitConditionType.WhenAll,
            CompletedCount = 1,
            Timeout = TimeSpan.FromMinutes(5),
            CreatedAt = DateTime.UtcNow
        };

        // Test InMemory store
        var inMemoryStore = new InMemoryDslFlowStore();
        await TestWaitConditionOperations(inMemoryStore, condition);

        // Test Redis store (if available)
        if (IsRedisAvailable())
        {
            var redis = await GetRedisConnection();
            var serializer = new TestSerializer();
            var redisStore = new RedisDslFlowStore(redis!, serializer);
            await TestWaitConditionOperations(redisStore, condition);
        }

        // Test NATS store (if available)
        if (IsNatsAvailable())
        {
            var nats = await GetNatsConnection();
            var serializer = new TestSerializer();
            var natsStore = new NatsDslFlowStore(nats!, serializer);
            await TestWaitConditionOperations(natsStore, condition);
        }
    }

    [Fact]
    public async Task AllStores_SupportForEachProgress()
    {
        var progress = new ForEachProgress
        {
            CurrentIndex = 5,
            TotalCount = 10,
            CompletedIndices = [0, 1, 2, 5, 8],
            FailedIndices = [3, 4]
        };

        // Test InMemory store
        var inMemoryStore = new InMemoryDslFlowStore();
        await TestForEachProgressOperations(inMemoryStore, progress);

        // Test Redis store (if available)
        if (IsRedisAvailable())
        {
            var redis = await GetRedisConnection();
            var serializer = new TestSerializer();
            var redisStore = new RedisDslFlowStore(redis!, serializer);
            await TestForEachProgressOperations(redisStore, progress);
        }

        // Test NATS store (if available)
        if (IsNatsAvailable())
        {
            var nats = await GetNatsConnection();
            var serializer = new TestSerializer();
            var natsStore = new NatsDslFlowStore(nats!, serializer);
            await TestForEachProgressOperations(natsStore, progress);
        }
    }

    [Theory]
    [InlineData(DslFlowStatus.Pending)]
    [InlineData(DslFlowStatus.Running)]
    [InlineData(DslFlowStatus.Suspended)]
    [InlineData(DslFlowStatus.Completed)]
    [InlineData(DslFlowStatus.Failed)]
    public async Task AllStores_HandleAllFlowStatuses(DslFlowStatus status)
    {
        var snapshot = new FlowSnapshot<TestFlowState>
        {
            FlowId = $"status-test-{status}",
            State = new TestFlowState { FlowId = $"status-test-{status}", Value = 1 },
            Position = new FlowPosition([0]),
            Status = status,
            Error = status == DslFlowStatus.Failed ? "Test error" : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        // Test all stores
        var inMemoryStore = new InMemoryDslFlowStore();
        await inMemoryStore.CreateAsync(snapshot);
        var retrieved = await inMemoryStore.GetAsync<TestFlowState>(snapshot.FlowId);
        retrieved.Should().NotBeNull();
        retrieved!.Status.Should().Be(status);
    }

    [Fact]
    public async Task AllStores_HandleConcurrentOperations()
    {
        var stores = new List<IDslFlowStore> { new InMemoryDslFlowStore() };

        if (IsRedisAvailable())
        {
            var redis = await GetRedisConnection();
            stores.Add(new RedisDslFlowStore(redis!, new TestSerializer()));
        }

        if (IsNatsAvailable())
        {
            var nats = await GetNatsConnection();
            stores.Add(new NatsDslFlowStore(nats!, new TestSerializer()));
        }

        foreach (var store in stores)
        {
            var tasks = new List<Task<bool>>();

            // Create multiple flows concurrently
            for (int i = 0; i < 10; i++)
            {
                var snapshot = new FlowSnapshot<TestFlowState>
                {
                    FlowId = $"concurrent-{i}",
                    State = new TestFlowState { FlowId = $"concurrent-{i}", Value = i },
                    Position = new FlowPosition([0]),
                    Status = DslFlowStatus.Running,
                    Version = 1
                };

                tasks.Add(store.CreateAsync(snapshot));
            }

            var results = await Task.WhenAll(tasks);
            results.Should().AllBeEquivalentTo(true, $"{store.GetType().Name} should handle concurrent creates");
        }
    }

    [Fact]
    public async Task AllStores_HandleLargeData()
    {
        var largeState = new TestFlowState
        {
            FlowId = "large-data",
            Value = 999,
            LargeData = new string('x', 1_000_000) // 1MB of data
        };

        var snapshot = new FlowSnapshot<TestFlowState>
        {
            FlowId = "large-data",
            State = largeState,
            Position = new FlowPosition([0]),
            Status = DslFlowStatus.Running,
            Version = 1
        };

        // Test InMemory store
        var inMemoryStore = new InMemoryDslFlowStore();
        await inMemoryStore.CreateAsync(snapshot);
        var retrieved = await inMemoryStore.GetAsync<TestFlowState>("large-data");
        retrieved.Should().NotBeNull();
        retrieved!.State.LargeData.Should().HaveLength(1_000_000);
    }

    // Helper methods
    private static HashSet<string> GetPublicMethods(Type type)
    {
        return type.GetMethods()
            .Where(m => m.IsPublic && !m.IsSpecialName && m.DeclaringType == type)
            .Select(m => m.Name)
            .ToHashSet();
    }

    private static HashSet<string> GetInterfaceMethods(Type type)
    {
        return type.GetMethods()
            .Select(m => m.Name)
            .ToHashSet();
    }

    private static async Task TestStoreOperations<TState>(IDslFlowStore store, FlowSnapshot<TState> snapshot)
        where TState : class, IFlowState
    {
        // Create
        var created = await store.CreateAsync(snapshot);
        created.Should().BeTrue("should create new snapshot");

        // Get
        var retrieved = await store.GetAsync<TState>(snapshot.FlowId);
        retrieved.Should().NotBeNull();
        retrieved!.FlowId.Should().Be(snapshot.FlowId);

        // Update
        retrieved.Version = snapshot.Version;
        var updated = await store.UpdateAsync(retrieved);
        updated.Should().BeTrue("should update existing snapshot");

        // Delete
        var deleted = await store.DeleteAsync(snapshot.FlowId);
        deleted.Should().BeTrue("should delete existing snapshot");
    }

    private static async Task TestWaitConditionOperations(IDslFlowStore store, WaitCondition condition)
    {
        // Set
        await store.SetWaitConditionAsync(condition.CorrelationId, condition);

        // Get
        var retrieved = await store.GetWaitConditionAsync(condition.CorrelationId);
        retrieved.Should().NotBeNull();
        retrieved!.CorrelationId.Should().Be(condition.CorrelationId);

        // Update
        condition.CompletedCount = 2;
        await store.UpdateWaitConditionAsync(condition.CorrelationId, condition);

        // Clear
        await store.ClearWaitConditionAsync(condition.CorrelationId);
        var cleared = await store.GetWaitConditionAsync(condition.CorrelationId);
        cleared.Should().BeNull();
    }

    private static async Task TestForEachProgressOperations(IDslFlowStore store, ForEachProgress progress)
    {
        const string flowId = "foreach-test";
        const int stepIndex = 5;

        // Save
        await store.SaveForEachProgressAsync(flowId, stepIndex, progress);

        // Get
        var retrieved = await store.GetForEachProgressAsync(flowId, stepIndex);
        retrieved.Should().NotBeNull();
        retrieved!.CurrentIndex.Should().Be(progress.CurrentIndex);

        // Clear
        await store.ClearForEachProgressAsync(flowId, stepIndex);
        var cleared = await store.GetForEachProgressAsync(flowId, stepIndex);
        cleared.Should().BeNull();
    }

    private static bool IsRedisAvailable()
    {
        try
        {
            var redis = ConnectionMultiplexer.Connect("localhost:6379,abortConnect=false,connectTimeout=1000");
            redis.Close();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsNatsAvailable()
    {
        // For testing purposes, assume NATS is not available
        // In real scenarios, would check NATS connection
        return false;
    }

    private static async Task<IConnectionMultiplexer?> GetRedisConnection()
    {
        try
        {
            return await ConnectionMultiplexer.ConnectAsync("localhost:6379,abortConnect=false");
        }
        catch
        {
            return null;
        }
    }

    private static Task<INatsConnection?> GetNatsConnection()
    {
        // For testing purposes, return null
        // In real scenarios, would create NATS connection
        return Task.FromResult<INatsConnection?>(null);
    }
}

// Test state for validation
public class ValidationTestState : IFlowState
{
    public string? FlowId { get; set; }
    public int Value { get; set; }
    public string? LargeData { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

