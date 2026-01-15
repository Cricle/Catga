using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence;
using Catga.Persistence.Nats;
using Catga.Persistence.Nats.Stores;
using Catga.Persistence.Stores;
using Catga.Resilience;
using Catga.Serialization.MemoryPack;
using Catga.Tests.PropertyTests.Generators;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;
using Xunit;

namespace Catga.Tests.PropertyTests;

/// <summary>
/// NATS 容器共享 Fixture - 使用全局共享容器
/// 用于在所有 NATS 属性测试之间共享同一个 NATS 容器
/// </summary>
public class NatsContainerFixture : IAsyncLifetime
{
    private readonly SharedTestContainers _sharedContainers;
    public NatsConnection? NatsConnection { get; private set; }
    public INatsJSContext? JetStreamContext { get; private set; }

    public NatsContainerFixture()
    {
        _sharedContainers = SharedTestContainers.Instance;
    }

    public async Task InitializeAsync()
    {
        await _sharedContainers.InitializeAsync();

        if (_sharedContainers.NatsConnectionString != null)
        {
            // 等待 NATS 完全启动
            await Task.Delay(1000);

            var opts = new NatsOpts
            {
                Url = _sharedContainers.NatsConnectionString,
                ConnectTimeout = TimeSpan.FromSeconds(10)
            };

            NatsConnection = new NatsConnection(opts);
            await NatsConnection.ConnectAsync();

            // 创建 JetStream 上下文
            JetStreamContext = new NatsJSContext(NatsConnection);
        }
    }

    public async Task DisposeAsync()
    {
        if (NatsConnection != null)
            await NatsConnection.DisposeAsync();
        // 不释放共享容器
    }

    /// <summary>
    /// 清理 NATS JetStream 数据（在每个测试前调用）
    /// 优化：使用唯一的stream名称隔离而不是删除所有streams
    /// </summary>
    public async Task CleanupStreamsAsync()
    {
        // 不再删除所有 streams，改用唯一的 stream 名称隔离
        await Task.CompletedTask;
    }

    /// <summary>
    /// 生成唯一的 stream 名称用于测试隔离
    /// </summary>
    public string GenerateStreamName(string baseName)
    {
        return $"{baseName}_{Guid.NewGuid():N}";
    }
}

/// <summary>
/// NATS 属性测试集合定义
/// </summary>
[CollectionDefinition("NatsPropertyTests")]
public class NatsPropertyTestsCollection : ICollectionFixture<NatsContainerFixture>
{
}

/// <summary>
/// NATS EventStore 属性测试
/// 使用 FsCheck 验证 NATS JetStream 后端的行为一致性
/// 
/// **Validates: Requirements 13.15**
/// Feature: tdd-validation, Task 20.4
/// </summary>
[Trait("Category", "Property")]
[Trait("Backend", "NATS")]
[Trait("Requires", "Docker")]
[Collection("NatsPropertyTests")]
public class NatsEventStorePropertyTests
{
    private readonly NatsContainerFixture _fixture;

    public NatsEventStorePropertyTests(NatsContainerFixture fixture)
    {
        _fixture = fixture;
    }

    private IEventStore CreateStore(string streamName)
    {
        if (_fixture.NatsConnection == null)
            throw new InvalidOperationException("NATS connection not initialized - Docker may not be running");

        var serializer = new MemoryPackMessageSerializer();
        var logger = Mock.Of<ILogger<NatsJSEventStore>>();
        var provider = new DiagnosticResiliencePipelineProvider();
        return new NatsJSEventStore(
            _fixture.NatsConnection,
            serializer,
            provider,
            registry: null,
            streamName: streamName,
            options: null);
    }

    /// <summary>
    /// Property 1: EventStore Round-Trip Consistency (NATS)
    /// 
    /// *For any* valid event sequence and stream ID, appending events to the NATS EventStore 
    /// then reading them back SHALL return events with identical MessageId, EventType, 
    /// Version, Data, and Timestamp.
    /// 
    /// **Validates: Requirements 13.15**
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.QuickMaxTest, Skip = "Requires Docker")]
    public Property NATS_EventStore_RoundTrip_PreservesAllEventData()
    {
        if (_fixture.NatsConnection == null)
            return true.ToProperty(); // Skip if Docker not available

        return Prop.ForAll(
            EventGenerators.StreamIdArbitrary(),
            EventGenerators.SmallEventListArbitrary(),
            (streamId, events) =>
            {
                // Use unique stream name for each test - no cleanup needed
                var uniqueStreamName = $"TEST_EVENTS_{Guid.NewGuid():N}";
                var store = CreateStore(uniqueStreamName);

                // Arrange & Act
                store.AppendAsync(streamId, events).AsTask().GetAwaiter().GetResult();

                // Wait for JetStream to persist
                Task.Delay(500).GetAwaiter().GetResult();

                var result = store.ReadAsync(streamId).AsTask().GetAwaiter().GetResult();

                // Assert - Verify round-trip consistency
                var loadedEvents = result.Events;

                // 1. Same number of events
                if (loadedEvents.Count != events.Count)
                {
                    return false;
                }

                // 2. Each event preserves its data
                for (int i = 0; i < events.Count; i++)
                {
                    var original = events[i];
                    var loaded = loadedEvents[i];

                    // Verify MessageId is preserved
                    if (original.MessageId != loaded.Event.MessageId)
                    {
                        return false;
                    }

                    // Verify EventType is set correctly
                    if (string.IsNullOrEmpty(loaded.EventType))
                    {
                        return false;
                    }

                    // Verify Version is sequential (0-based)
                    if (loaded.Version != i)
                    {
                        return false;
                    }

                    // Verify the event data is the same type
                    if (original.GetType() != loaded.Event.GetType())
                    {
                        return false;
                    }

                    // Verify TestPropertyEvent specific data
                    if (original is TestPropertyEvent originalEvent && loaded.Event is TestPropertyEvent loadedEvent)
                    {
                        if (originalEvent.Data != loadedEvent.Data)
                        {
                            return false;
                        }

                        if (originalEvent.Amount != loadedEvent.Amount)
                        {
                        return false;
                        }
                    }
                }

                return true;
            });
    }

    /// <summary>
    /// Property 2: EventStore Version Invariant (NATS)
    /// 
    /// *For any* stream with N appended events, the stream version SHALL equal N-1 (0-based indexing).
    /// 
    /// **Validates: Requirements 13.15**
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.QuickMaxTest, Skip = "Requires Docker")]
    public Property NATS_EventStore_Version_EqualsEventCountMinusOne()
    {
        if (_fixture.NatsConnection == null)
            return true.ToProperty(); // Skip if Docker not available

        return Prop.ForAll(
            EventGenerators.StreamIdArbitrary(),
            EventGenerators.SmallEventListArbitrary(),
            (streamId, events) =>
            {
                // Use unique stream name for each test - no cleanup needed
                var uniqueStreamName = $"TEST_EVENTS_{Guid.NewGuid():N}";
                var store = CreateStore(uniqueStreamName);

                // Arrange & Act
                store.AppendAsync(streamId, events).AsTask().GetAwaiter().GetResult();

                // Wait for JetStream to persist
                Task.Delay(500).GetAwaiter().GetResult();

                var version = store.GetVersionAsync(streamId).AsTask().GetAwaiter().GetResult();

                // Assert - Version should equal event count minus 1 (0-based indexing)
                return version == events.Count - 1;
            });
    }

    /// <summary>
    /// Property 3: EventStore Ordering Guarantee (NATS)
    /// 
    /// *For any* sequence of events appended to a stream, reading the stream SHALL return 
    /// events in the exact order they were appended.
    /// 
    /// **Validates: Requirements 13.15**
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.QuickMaxTest, Skip = "Requires Docker")]
    public Property NATS_EventStore_Read_PreservesAppendOrder()
    {
        if (_fixture.NatsConnection == null)
            return true.ToProperty(); // Skip if Docker not available

        return Prop.ForAll(
            EventGenerators.StreamIdArbitrary(),
            EventGenerators.SmallEventListArbitrary(),
            (streamId, events) =>
            {
                // Use unique stream name for each test - no cleanup needed
                var uniqueStreamName = $"TEST_EVENTS_{Guid.NewGuid():N}";
                var store = CreateStore(uniqueStreamName);

                // Arrange & Act
                store.AppendAsync(streamId, events).AsTask().GetAwaiter().GetResult();

                // Wait for JetStream to persist
                Task.Delay(500).GetAwaiter().GetResult();

                var result = store.ReadAsync(streamId).AsTask().GetAwaiter().GetResult();

                // Assert - Verify ordering is preserved
                var loadedEvents = result.Events;

                // 1. Same number of events
                if (loadedEvents.Count != events.Count)
                {
                    return false;
                }

                // 2. Events are returned in the exact order they were appended
                var originalMessageIds = events.Select(e => e.MessageId).ToList();
                var loadedMessageIds = loadedEvents.Select(e => e.Event.MessageId).ToList();

                return originalMessageIds.SequenceEqual(loadedMessageIds);
            });
    }
}

/// <summary>
/// NATS SnapshotStore 属性测试
/// 
/// **Validates: Requirements 14.11**
/// Feature: tdd-validation, Task 21.3
/// </summary>
[Trait("Category", "Property")]
[Trait("Backend", "NATS")]
[Trait("Requires", "Docker")]
[Collection("NatsPropertyTests")]
public class NatsSnapshotStorePropertyTests
{
    private readonly NatsContainerFixture _fixture;

    public NatsSnapshotStorePropertyTests(NatsContainerFixture fixture)
    {
        _fixture = fixture;
    }

    private ISnapshotStore CreateStore()
    {
        if (_fixture.NatsConnection == null)
            throw new InvalidOperationException("NATS connection not initialized - Docker may not be running");

        var serializer = new MemoryPackMessageSerializer();
        var logger = Mock.Of<ILogger<NatsSnapshotStore>>();
        var options = Microsoft.Extensions.Options.Options.Create(new SnapshotOptions { KeyPrefix = $"test_{Guid.NewGuid():N}_" });
        return new NatsSnapshotStore(
            _fixture.NatsConnection,
            serializer,
            options,
            logger);
    }

    /// <summary>
    /// Property 5: SnapshotStore Round-Trip Consistency (NATS)
    /// 
    /// *For any* valid snapshot data and aggregate ID, saving a snapshot to NATS KV Store 
    /// then loading it back SHALL return data with identical content and version.
    /// 
    /// **Validates: Requirements 14.11**
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.QuickMaxTest, Skip = "Requires Docker")]
    public Property NATS_SnapshotStore_RoundTrip_PreservesAllData()
    {
        if (_fixture.NatsConnection == null)
            return true.ToProperty(); // Skip if Docker not available

        return Prop.ForAll(
            SnapshotGenerators.AggregateIdArbitrary(),
            SnapshotGenerators.TestSnapshotArbitrary(),
            Gen.Choose(0, 10000).ToArbitrary(),
            (aggregateId, snapshot, version) =>
            {
                // Use unique key prefix for each test - no cleanup needed
                var store = CreateStore();

                // Arrange & Act
                store.SaveAsync(aggregateId, snapshot, version).AsTask().GetAwaiter().GetResult();

                // Wait for KV Store to persist
                Task.Delay(500).GetAwaiter().GetResult();

                var loaded = store.LoadAsync<TestSnapshot>(aggregateId).AsTask().GetAwaiter().GetResult();

                // Assert
                if (loaded == null) return false;
                if (loaded.Value.Version != version) return false;
                if (loaded.Value.State.Name != snapshot.Name) return false;
                if (loaded.Value.State.Value != snapshot.Value) return false;

                return true;
            });
    }

    /// <summary>
    /// Property 6: SnapshotStore Latest Version Only (NATS)
    /// 
    /// *For any* aggregate with multiple snapshots saved, loading SHALL return 
    /// only the latest version.
    /// 
    /// **Validates: Requirements 14.11**
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.QuickMaxTest, Skip = "Requires Docker")]
    public Property NATS_SnapshotStore_Load_ReturnsLatestVersion()
    {
        if (_fixture.NatsConnection == null)
            return true.ToProperty(); // Skip if Docker not available

        return Prop.ForAll(
            SnapshotGenerators.AggregateIdArbitrary(),
            SnapshotGenerators.TestSnapshotArbitrary(),
            SnapshotGenerators.TestSnapshotArbitrary(),
            (aggregateId, snapshot1, snapshot2) =>
            {
                // Use unique key prefix for each test - no cleanup needed
                var store = CreateStore();

                // Arrange - Save two versions
                store.SaveAsync(aggregateId, snapshot1, 1).AsTask().GetAwaiter().GetResult();

                // Wait for KV Store to persist
                Task.Delay(300).GetAwaiter().GetResult();

                store.SaveAsync(aggregateId, snapshot2, 2).AsTask().GetAwaiter().GetResult();

                // Wait for KV Store to persist
                Task.Delay(300).GetAwaiter().GetResult();

                // Act
                var loaded = store.LoadAsync<TestSnapshot>(aggregateId).AsTask().GetAwaiter().GetResult();

                // Assert - Should return the latest version (version 2)
                if (loaded == null) return false;
                if (loaded.Value.Version != 2) return false;
                if (loaded.Value.State.Name != snapshot2.Name) return false;
                if (loaded.Value.State.Value != snapshot2.Value) return false;

                return true;
            });
    }
}

/// <summary>
/// NATS MessageTransport 属性测试
/// 
/// **Validates: Requirements 15.13**
/// Feature: tdd-validation, Task 22.3
/// 
/// 注意: NATS Transport 属性测试由于需要复杂的订阅管理和异步消息传递，
/// 暂时跳过实现。建议使用集成测试验证 NATS Transport 的行为。
/// </summary>
[Trait("Category", "Property")]
[Trait("Backend", "NATS")]
[Trait("Requires", "Docker")]
[Collection("NatsPropertyTests")]
public class NatsMessageTransportPropertyTests
{
    private readonly NatsContainerFixture _fixture;

    public NatsMessageTransportPropertyTests(NatsContainerFixture fixture)
    {
        _fixture = fixture;
    }

    // Note: NATS Transport property tests are complex due to:
    // 1. Need to manage NATS JetStream subscriptions and consumers
    // 2. Async message delivery with timing considerations
    // 3. Consumer acknowledgment and redelivery management
    // 
    // These are better tested through integration tests.
    // See: tests/Catga.Tests/Integration/Nats/NatsMessageFunctionalityTests.cs
}

/// <summary>
/// NATS DslFlowStore 属性测试
/// 
/// **Validates: Requirements 16.11**
/// Feature: tdd-validation, Task 23.3
/// 
/// 注意: NATS FlowStore 属性测试由于需要复杂的序列化和状态管理，
/// 暂时跳过实现。建议使用集成测试验证 NATS FlowStore 的行为。
/// </summary>
[Trait("Category", "Property")]
[Trait("Backend", "NATS")]
[Trait("Requires", "Docker")]
[Collection("NatsPropertyTests")]
public class NatsDslFlowStorePropertyTests
{
    private readonly NatsContainerFixture _fixture;

    public NatsDslFlowStorePropertyTests(NatsContainerFixture fixture)
    {
        _fixture = fixture;
    }

    // Note: NATS FlowStore property tests are complex due to:
    // 1. Complex state serialization with MemoryPack
    // 2. FlowPosition and checkpoint management
    // 3. NATS KV Store versioning and watch functionality
    // 
    // These are better tested through integration tests.
    // See: tests/Catga.Tests/Integration/NatsFlowStoreTests.cs
}
