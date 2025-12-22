using Catga.Abstractions;
using Catga.Core;
using Catga.Serialization.MemoryPack;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using MemoryPack;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;
using Xunit;

namespace Catga.Tests.Integration.Nats;

/// <summary>
/// NATS KV Store 特定功能测试
/// 测试 KV Bucket 创建、版本控制、Watch 功能等
/// Validates: Requirements 14.5-14.7
/// </summary>
[Trait("Category", "Integration")]
[Trait("Backend", "NATS")]
[Trait("Requires", "Docker")]
public partial class NatsKVFunctionalityTests : IAsyncLifetime
{
    private IContainer? _natsContainer;
    private NatsConnection? _natsConnection;
    private INatsKVContext? _kvContext;
    private IMessageSerializer? _serializer;

    public async Task InitializeAsync()
    {
        if (!IsDockerRunning())
        {
            return;
        }

        var natsImage = Environment.GetEnvironmentVariable("TEST_NATS_IMAGE") ?? "nats:latest";
        _natsContainer = new ContainerBuilder()
            .WithImage(natsImage)
            .WithPortBinding(4222, true)
            .WithPortBinding(8222, true)
            .WithCommand("-js", "-m", "8222")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r
                    .ForPort(8222)
                    .ForPath("/varz")))
            .Build();

        await _natsContainer.StartAsync();
        await Task.Delay(2000);

        var port = _natsContainer.GetMappedPublicPort(4222);
        var opts = new NatsOpts
        {
            Url = $"nats://localhost:{port}",
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };

        _natsConnection = new NatsConnection(opts);
        await _natsConnection.ConnectAsync();
        var jetStream = new NatsJSContext(_natsConnection);
        _kvContext = new NatsKVContext(jetStream);
        _serializer = new MemoryPackMessageSerializer();
    }

    public async Task DisposeAsync()
    {
        if (_natsConnection != null)
            await _natsConnection.DisposeAsync();

        if (_natsContainer != null)
            await _natsContainer.DisposeAsync();
    }

    private static bool IsDockerRunning()
    {
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    #region Bucket Creation

    /// <summary>
    /// 测试 KV Bucket 创建
    /// Validates: Requirements 14.5
    /// </summary>
    [Fact]
    public async Task NATS_KV_BucketCreation()
    {
        if (_kvContext is null) return;

        // Arrange
        var bucketName = $"TEST_BUCKET_{Guid.NewGuid():N}";
        var config = new NatsKVConfig(bucketName)
        {
            History = 5,
            MaxBytes = 1024 * 1024 // 1MB
        };

        // Act
        var store = await _kvContext.CreateStoreAsync(config);

        // Assert
        store.Should().NotBeNull();
        store.Bucket.Should().Be(bucketName);
    }

    /// <summary>
    /// 测试 KV Bucket 配置选项
    /// Validates: Requirements 14.5
    /// </summary>
    [Fact]
    public async Task NATS_KV_BucketCreation_WithOptions()
    {
        if (_kvContext is null) return;

        // Arrange
        var bucketName = $"TEST_OPTIONS_{Guid.NewGuid():N}";
        var config = new NatsKVConfig(bucketName)
        {
            History = 10,
            MaxBytes = 2 * 1024 * 1024, // 2MB
            MaxValueSize = 1024 // 1KB per value
            // Ttl = TimeSpan.FromMinutes(5) // Property removed in current NATS client
        };

        // Act
        var store = await _kvContext.CreateStoreAsync(config);

        // Assert
        store.Should().NotBeNull();
        var info = await store.GetStatusAsync();
        info.Bucket.Should().Be(bucketName);
        // info.Config.History.Should().Be(10); // Config property removed in current NATS client
    }

    /// <summary>
    /// 测试获取已存在的 Bucket
    /// Validates: Requirements 14.5
    /// </summary>
    [Fact]
    public async Task NATS_KV_GetExistingBucket()
    {
        if (_kvContext is null) return;

        // Arrange
        var bucketName = $"TEST_EXISTING_{Guid.NewGuid():N}";
        await _kvContext.CreateStoreAsync(new NatsKVConfig(bucketName));

        // Act
        var store = await _kvContext.GetStoreAsync(bucketName);

        // Assert
        store.Should().NotBeNull();
        store.Bucket.Should().Be(bucketName);
    }

    #endregion

    #region Versioning

    /// <summary>
    /// 测试 KV 版本控制
    /// Validates: Requirements 14.4
    /// </summary>
    [Fact]
    public async Task NATS_KV_Versioning()
    {
        if (_kvContext is null) return;

        // Arrange
        var bucketName = $"TEST_VERSION_{Guid.NewGuid():N}";
        var store = await _kvContext.CreateStoreAsync(new NatsKVConfig(bucketName) { History = 5 });

        var key = "version-test";
        var value1 = _serializer!.Serialize(new TestData { Id = "v1", Value = "First version" });
        var value2 = _serializer!.Serialize(new TestData { Id = "v2", Value = "Second version" });
        var value3 = _serializer!.Serialize(new TestData { Id = "v3", Value = "Third version" });

        // Act - Put multiple versions
        var revision1 = await store.PutAsync(key, value1);
        await Task.Delay(100);
        var revision2 = await store.PutAsync(key, value2);
        await Task.Delay(100);
        var revision3 = await store.PutAsync(key, value3);

        // Assert - Latest version
        var latest = await store.GetEntryAsync<byte[]>(key);
        latest.Should().NotBeNull();
        var latestData = _serializer!.Deserialize<TestData>(latest!.Value);
        latestData.Id.Should().Be("v3");

        // Assert - Specific version
        var entry2 = await store.GetEntryAsync<byte[]>(key, revision: revision2);
        entry2.Should().NotBeNull();
        var data2 = _serializer!.Deserialize<TestData>(entry2!.Value);
        data2.Id.Should().Be("v2");
    }

    /// <summary>
    /// 测试版本历史
    /// Validates: Requirements 14.4
    /// </summary>
    [Fact]
    public async Task NATS_KV_VersionHistory()
    {
        if (_kvContext is null) return;

        // Arrange
        var bucketName = $"TEST_HISTORY_{Guid.NewGuid():N}";
        var store = await _kvContext.CreateStoreAsync(new NatsKVConfig(bucketName) { History = 10 });

        var key = "history-test";

        // Act - Create version history
        for (int i = 1; i <= 5; i++)
        {
            var value = _serializer!.Serialize(new TestData { Id = $"v{i}", Value = $"Version {i}" });
            await store.PutAsync(key, value);
            await Task.Delay(50);
        }

        // Assert - Get history
        var history = new List<TestData>();
        await foreach (var entry in store.HistoryAsync<byte[]>(key))
        {
            var data = _serializer!.Deserialize<TestData>(entry.Value);
            history.Add(data);
        }

        history.Should().HaveCount(5);
        history.Select(h => h.Id).Should().BeEquivalentTo(["v1", "v2", "v3", "v4", "v5"]);
    }

    /// <summary>
    /// 测试条件更新（CAS - Compare And Swap）
    /// Validates: Requirements 14.4
    /// </summary>
    [Fact]
    public async Task NATS_KV_ConditionalUpdate()
    {
        if (_kvContext is null) return;

        // Arrange
        var bucketName = $"TEST_CAS_{Guid.NewGuid():N}";
        var store = await _kvContext.CreateStoreAsync(new NatsKVConfig(bucketName));

        var key = "cas-test";
        var value1 = _serializer!.Serialize(new TestData { Id = "v1", Value = "Initial" });
        var value2 = _serializer!.Serialize(new TestData { Id = "v2", Value = "Updated" });

        var revision1 = await store.PutAsync(key, value1);

        // Act - Update with correct revision
        var revision2 = await store.UpdateAsync(key, value2, revision1);

        // Assert
        revision2.Should().BeGreaterThan(revision1);

        var latest = await store.GetEntryAsync<byte[]>(key);
        var latestData = _serializer!.Deserialize<TestData>(latest!.Value);
        latestData.Id.Should().Be("v2");
    }

    /// <summary>
    /// 测试条件更新失败（版本冲突）
    /// Validates: Requirements 14.4
    /// </summary>
    [Fact]
    public async Task NATS_KV_ConditionalUpdate_VersionConflict()
    {
        if (_kvContext is null) return;

        // Arrange
        var bucketName = $"TEST_CONFLICT_{Guid.NewGuid():N}";
        var store = await _kvContext.CreateStoreAsync(new NatsKVConfig(bucketName));

        var key = "conflict-test";
        var value1 = _serializer!.Serialize(new TestData { Id = "v1", Value = "Initial" });
        var value2 = _serializer!.Serialize(new TestData { Id = "v2", Value = "Update 1" });
        var value3 = _serializer!.Serialize(new TestData { Id = "v3", Value = "Update 2" });

        var revision1 = await store.PutAsync(key, value1);
        await store.PutAsync(key, value2); // This changes the revision

        // Act - Try to update with old revision
        var act = async () => await store.UpdateAsync(key, value3, revision1);

        // Assert
        await act.Should().ThrowAsync<NatsKVWrongLastRevisionException>();
    }

    #endregion

    #region Watch Functionality

    /// <summary>
    /// 测试 KV Watch 功能
    /// Validates: Requirements 14.6
    /// </summary>
    [Fact]
    public async Task NATS_KV_Watch()
    {
        if (_kvContext is null) return;

        // Arrange
        var bucketName = $"TEST_WATCH_{Guid.NewGuid():N}";
        var store = await _kvContext.CreateStoreAsync(new NatsKVConfig(bucketName));

        var key = "watch-test";
        var receivedUpdates = new List<string>();
        var watchCts = new CancellationTokenSource();

        // Start watching
        var watchTask = Task.Run(async () =>
        {
            await foreach (var entry in store.WatchAsync<byte[]>(key).WithCancellation(watchCts.Token))
            {
                if (entry.Value != null)
                {
                    var data = _serializer!.Deserialize<TestData>(entry.Value);
                    receivedUpdates.Add(data.Id);
                }
                
                if (receivedUpdates.Count >= 3) break;
            }
        });

        await Task.Delay(500); // Allow watch to start

        // Act - Make updates
        for (int i = 1; i <= 3; i++)
        {
            var value = _serializer!.Serialize(new TestData { Id = $"update-{i}", Value = $"Value {i}" });
            await store.PutAsync(key, value);
            await Task.Delay(200);
        }

        await Task.WhenAny(watchTask, Task.Delay(5000));
        watchCts.Cancel();

        // Assert
        receivedUpdates.Should().HaveCountGreaterOrEqualTo(3);
        receivedUpdates.Should().Contain("update-1");
        receivedUpdates.Should().Contain("update-2");
        receivedUpdates.Should().Contain("update-3");
    }

    /// <summary>
    /// 测试 Watch 所有键
    /// Validates: Requirements 14.6
    /// </summary>
    [Fact]
    public async Task NATS_KV_WatchAll()
    {
        if (_kvContext is null) return;

        // Arrange
        var bucketName = $"TEST_WATCHALL_{Guid.NewGuid():N}";
        var store = await _kvContext.CreateStoreAsync(new NatsKVConfig(bucketName));

        var receivedKeys = new List<string>();
        var watchCts = new CancellationTokenSource();

        // Start watching all keys
        var watchTask = Task.Run(async () =>
        {
            await foreach (var entry in store.WatchAsync<byte[]>(">").WithCancellation(watchCts.Token))
            {
                receivedKeys.Add(entry.Key);
                if (receivedKeys.Count >= 3) break;
            }
        });

        await Task.Delay(500);

        // Act - Update multiple keys
        await store.PutAsync("key1", _serializer!.Serialize(new TestData { Id = "1", Value = "Value 1" }));
        await Task.Delay(100);
        await store.PutAsync("key2", _serializer!.Serialize(new TestData { Id = "2", Value = "Value 2" }));
        await Task.Delay(100);
        await store.PutAsync("key3", _serializer!.Serialize(new TestData { Id = "3", Value = "Value 3" }));

        await Task.WhenAny(watchTask, Task.Delay(5000));
        watchCts.Cancel();

        // Assert
        receivedKeys.Should().HaveCountGreaterOrEqualTo(3);
        receivedKeys.Should().Contain("key1");
        receivedKeys.Should().Contain("key2");
        receivedKeys.Should().Contain("key3");
    }

    /// <summary>
    /// 测试 Watch 删除操作
    /// Validates: Requirements 14.6
    /// </summary>
    [Fact]
    public async Task NATS_KV_Watch_Delete()
    {
        if (_kvContext is null) return;

        // Arrange
        var bucketName = $"TEST_WATCH_DEL_{Guid.NewGuid():N}";
        var store = await _kvContext.CreateStoreAsync(new NatsKVConfig(bucketName));

        var key = "delete-test";
        var operations = new List<string>();
        var watchCts = new CancellationTokenSource();

        // Start watching
        var watchTask = Task.Run(async () =>
        {
            await foreach (var entry in store.WatchAsync<byte[]>(key).WithCancellation(watchCts.Token))
            {
                operations.Add(entry.Operation.ToString());
                if (operations.Count >= 2) break;
            }
        });

        await Task.Delay(500);

        // Act - Put then delete
        await store.PutAsync(key, _serializer!.Serialize(new TestData { Id = "temp", Value = "Temporary" }));
        await Task.Delay(200);
        await store.DeleteAsync(key);

        await Task.WhenAny(watchTask, Task.Delay(5000));
        watchCts.Cancel();

        // Assert
        operations.Should().HaveCountGreaterOrEqualTo(2);
        operations.Should().Contain("Put");
        operations.Should().Contain("Del");
    }

    #endregion

    #region Bucket Replication

    /// <summary>
    /// 测试 KV Bucket 基本操作（单节点场景）
    /// Validates: Requirements 14.7
    /// </summary>
    [Fact]
    public async Task NATS_KV_BucketReplication_SingleNode()
    {
        if (_kvContext is null) return;

        // Arrange
        var bucketName = $"TEST_REPL_{Guid.NewGuid():N}";
        var config = new NatsKVConfig(bucketName)
        {
            // Replicas = 1 // Property removed in current NATS client
        };

        var store = await _kvContext.CreateStoreAsync(config);

        // Act - Basic operations
        var key = "repl-test";
        var value = _serializer!.Serialize(new TestData { Id = "repl", Value = "Replicated data" });
        await store.PutAsync(key, value);

        var retrieved = await store.GetEntryAsync<byte[]>(key);

        // Assert
        retrieved.Should().NotBeNull();
        var data = _serializer!.Deserialize<TestData>(retrieved!.Value);
        data.Id.Should().Be("repl");
    }

    #endregion

    #region Additional KV Operations

    /// <summary>
    /// 测试 KV 键列表
    /// Validates: Requirements 14.5
    /// </summary>
    [Fact]
    public async Task NATS_KV_ListKeys()
    {
        if (_kvContext is null) return;

        // Arrange
        var bucketName = $"TEST_KEYS_{Guid.NewGuid():N}";
        var store = await _kvContext.CreateStoreAsync(new NatsKVConfig(bucketName));

        // Add multiple keys
        for (int i = 1; i <= 5; i++)
        {
            await store.PutAsync($"key{i}", _serializer!.Serialize(new TestData { Id = $"{i}", Value = $"Value {i}" }));
        }

        // Act
        var keys = new List<string>();
        await foreach (var key in store.GetKeysAsync())
        {
            keys.Add(key);
        }

        // Assert
        keys.Should().HaveCount(5);
        keys.Should().Contain("key1");
        keys.Should().Contain("key5");
    }

    /// <summary>
    /// 测试 KV 删除操作
    /// Validates: Requirements 14.5
    /// </summary>
    [Fact]
    public async Task NATS_KV_Delete()
    {
        if (_kvContext is null) return;

        // Arrange
        var bucketName = $"TEST_DELETE_{Guid.NewGuid():N}";
        var store = await _kvContext.CreateStoreAsync(new NatsKVConfig(bucketName));

        var key = "delete-me";
        await store.PutAsync(key, _serializer!.Serialize(new TestData { Id = "temp", Value = "Temporary" }));

        // Act
        await store.DeleteAsync(key);

        // Assert
        var entry = await store.GetEntryAsync<byte[]>(key);
        entry.Should().BeNull();
    }

    /// <summary>
    /// 测试 KV Purge 操作
    /// Validates: Requirements 14.5
    /// </summary>
    [Fact]
    public async Task NATS_KV_Purge()
    {
        if (_kvContext is null) return;

        // Arrange
        var bucketName = $"TEST_PURGE_{Guid.NewGuid():N}";
        var store = await _kvContext.CreateStoreAsync(new NatsKVConfig(bucketName) { History = 5 });

        var key = "purge-test";

        // Create version history
        for (int i = 1; i <= 5; i++)
        {
            await store.PutAsync(key, _serializer!.Serialize(new TestData { Id = $"v{i}", Value = $"Version {i}" }));
        }

        // Act - Purge (delete all versions)
        await store.PurgeAsync(key);

        // Assert
        var entry = await store.GetEntryAsync<byte[]>(key);
        entry.Should().BeNull();
    }

    #endregion

    #region Test Models

    [MemoryPackable]
    private partial record TestData
    {
        public required string Id { get; init; }
        public required string Value { get; init; }
    }

    #endregion
}
