using Catga.Abstractions;
using Catga.Core;
using Catga.EventSourcing;
using Catga.Persistence.Redis.Stores;
using Catga.Resilience;
using Catga.Serialization.MemoryPack;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Catga.Tests.Integration.Redis;

/// <summary>
/// Redis 特定功能测试
/// 测试 Redis 事务、乐观锁、连接管理等特定功能
/// 
/// **Validates: Requirements 7.10-7.13, 17.1-17.4**
/// </summary>
[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
[Trait("Backend", "Redis")]
public sealed class RedisSpecificFunctionalityTests : IAsyncLifetime
{
    private RedisContainer? _container;
    private IConnectionMultiplexer? _redis;
    private readonly IMessageSerializer _serializer = new MemoryPackMessageSerializer();
    private readonly IResiliencePipelineProvider _provider = new DefaultResiliencePipelineProvider();

    public async Task InitializeAsync()
    {
        if (!IsDockerRunning()) return;
        
        _container = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();
        await _container.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (_redis is not null) await _redis.CloseAsync();
        if (_container is not null) await _container.DisposeAsync();
    }


    #region Redis Transaction Atomicity Tests (Requirement 7.10)

    /// <summary>
    /// Tests that Redis transactions are atomic - all operations succeed or none do.
    /// **Validates: Requirement 7.10 - Redis Transaction Atomicity**
    /// </summary>
    [Fact]
    public async Task Redis_Transaction_Atomicity_AllOperationsSucceed()
    {
        if (_redis is null) return;

        // Arrange
        var db = _redis.GetDatabase();
        var key1 = $"tx-test-{Guid.NewGuid():N}:key1";
        var key2 = $"tx-test-{Guid.NewGuid():N}:key2";

        // Act - Execute transaction with multiple operations
        var tran = db.CreateTransaction();
        var task1 = tran.StringSetAsync(key1, "value1");
        var task2 = tran.StringSetAsync(key2, "value2");
        var committed = await tran.ExecuteAsync();

        // Assert - All operations should succeed atomically
        committed.Should().BeTrue("transaction should commit successfully");
        
        var value1 = await db.StringGetAsync(key1);
        var value2 = await db.StringGetAsync(key2);
        
        value1.ToString().Should().Be("value1");
        value2.ToString().Should().Be("value2");
    }

    /// <summary>
    /// Tests that Redis transactions with WATCH fail on concurrent modification.
    /// **Validates: Requirement 7.11 - Redis Optimistic Locking with WATCH**
    /// </summary>
    [Fact]
    public async Task Redis_OptimisticLocking_WATCH_FailsOnConcurrentModification()
    {
        if (_redis is null) return;

        // Arrange
        var db = _redis.GetDatabase();
        var key = $"watch-test-{Guid.NewGuid():N}";
        await db.StringSetAsync(key, "initial");

        // Act - Start watching the key
        var tran = db.CreateTransaction();
        tran.AddCondition(Condition.StringEqual(key, "initial"));
        
        // Simulate concurrent modification by another client
        await db.StringSetAsync(key, "modified-by-other");
        
        // Try to execute transaction
        var task = tran.StringSetAsync(key, "my-value");
        var committed = await tran.ExecuteAsync();

        // Assert - Transaction should fail due to concurrent modification
        committed.Should().BeFalse("transaction should fail when watched key is modified");
    }

    /// <summary>
    /// Tests that Redis transactions succeed when watched key is not modified.
    /// **Validates: Requirement 7.11 - Redis Optimistic Locking with WATCH**
    /// </summary>
    [Fact]
    public async Task Redis_OptimisticLocking_WATCH_SucceedsWhenUnmodified()
    {
        if (_redis is null) return;

        // Arrange
        var db = _redis.GetDatabase();
        var key = $"watch-success-{Guid.NewGuid():N}";
        await db.StringSetAsync(key, "initial");

        // Act - Watch and modify without concurrent changes
        var tran = db.CreateTransaction();
        tran.AddCondition(Condition.StringEqual(key, "initial"));
        var task = tran.StringSetAsync(key, "updated");
        var committed = await tran.ExecuteAsync();

        // Assert
        committed.Should().BeTrue("transaction should succeed when watched key is unchanged");
        
        var value = await db.StringGetAsync(key);
        value.ToString().Should().Be("updated");
    }

    #endregion

    #region Redis Connection Management Tests (Requirements 17.1-17.4)

    /// <summary>
    /// Tests that Redis operations handle connection gracefully when available.
    /// **Validates: Requirement 17.1 - Redis Connection Handling**
    /// </summary>
    [Fact]
    public async Task Redis_Connection_OperationsSucceedWhenConnected()
    {
        if (_redis is null) return;

        // Arrange
        var store = new RedisEventStore(_redis, _serializer, _provider, NullLogger<RedisEventStore>.Instance);
        var streamId = $"conn-test-{Guid.NewGuid():N}";

        // Act
        await store.AppendAsync(streamId, [new RedisTestEvent { Name = "Test" }]);
        var result = await store.ReadAsync(streamId);

        // Assert
        result.Events.Should().HaveCount(1);
    }

    /// <summary>
    /// Tests that Redis multiplexer reports connection status correctly.
    /// **Validates: Requirement 17.2 - Redis Connection Status**
    /// </summary>
    [Fact]
    public void Redis_Connection_MultiplexerReportsStatus()
    {
        if (_redis is null) return;

        // Assert
        _redis.IsConnected.Should().BeTrue("multiplexer should report connected status");
        _redis.IsConnecting.Should().BeFalse("multiplexer should not be in connecting state");
    }

    /// <summary>
    /// Tests that Redis operations can be performed on multiple databases.
    /// **Validates: Requirement 17.3 - Redis Multi-Database Support**
    /// </summary>
    [Fact]
    public async Task Redis_MultiDatabase_OperationsAreIsolated()
    {
        if (_redis is null) return;

        // Arrange
        var db0 = _redis.GetDatabase(0);
        var db1 = _redis.GetDatabase(1);
        var key = $"multi-db-{Guid.NewGuid():N}";

        // Act
        await db0.StringSetAsync(key, "value-in-db0");
        await db1.StringSetAsync(key, "value-in-db1");

        // Assert - Values should be isolated per database
        var value0 = await db0.StringGetAsync(key);
        var value1 = await db1.StringGetAsync(key);

        value0.ToString().Should().Be("value-in-db0");
        value1.ToString().Should().Be("value-in-db1");
    }

    /// <summary>
    /// Tests that Redis pipeline operations execute efficiently.
    /// **Validates: Requirement 17.4 - Redis Pipeline Operations**
    /// </summary>
    [Fact]
    public async Task Redis_Pipeline_BatchOperationsExecuteEfficiently()
    {
        if (_redis is null) return;

        // Arrange
        var db = _redis.GetDatabase();
        var keyPrefix = $"pipeline-{Guid.NewGuid():N}";
        var tasks = new List<Task<bool>>();

        // Act - Execute multiple operations in pipeline (fire-and-forget style)
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(db.StringSetAsync($"{keyPrefix}:{i}", $"value-{i}"));
        }
        
        var results = await Task.WhenAll(tasks);

        // Assert - All operations should succeed
        results.Should().AllBeEquivalentTo(true);

        // Verify some values
        var value50 = await db.StringGetAsync($"{keyPrefix}:50");
        value50.ToString().Should().Be("value-50");
    }

    #endregion


    #region Redis Key Expiration Tests (Requirement 7.11)

    /// <summary>
    /// Tests that Redis keys can be set with TTL expiration.
    /// **Validates: Requirement 7.11 - Redis Key Expiration**
    /// </summary>
    [Fact]
    public async Task Redis_KeyExpiration_KeyExpiresAfterTTL()
    {
        if (_redis is null) return;

        // Arrange
        var db = _redis.GetDatabase();
        var key = $"ttl-test-{Guid.NewGuid():N}";
        var ttl = TimeSpan.FromSeconds(2);

        // Act
        await db.StringSetAsync(key, "value", ttl);
        var existsBefore = await db.KeyExistsAsync(key);
        
        // Wait for expiration
        await Task.Delay(TimeSpan.FromSeconds(3));
        var existsAfter = await db.KeyExistsAsync(key);

        // Assert
        existsBefore.Should().BeTrue("key should exist before TTL expires");
        existsAfter.Should().BeFalse("key should not exist after TTL expires");
    }

    #endregion

    #region Redis Lua Script Tests (Requirement 7.12)

    /// <summary>
    /// Tests that Redis Lua scripts execute atomically.
    /// **Validates: Requirement 7.12 - Redis Lua Script Atomicity**
    /// </summary>
    [Fact]
    public async Task Redis_LuaScript_ExecutesAtomically()
    {
        if (_redis is null) return;

        // Arrange
        var db = _redis.GetDatabase();
        var key = $"lua-test-{Guid.NewGuid():N}";
        await db.StringSetAsync(key, "0");

        // Lua script that increments a value atomically
        var script = @"
            local current = redis.call('GET', KEYS[1])
            local newValue = tonumber(current) + tonumber(ARGV[1])
            redis.call('SET', KEYS[1], newValue)
            return newValue
        ";

        // Act - Execute script multiple times
        var result1 = await db.ScriptEvaluateAsync(script, [(RedisKey)key], [(RedisValue)5]);
        var result2 = await db.ScriptEvaluateAsync(script, [(RedisKey)key], [(RedisValue)3]);
        var finalValue = await db.StringGetAsync(key);

        // Assert
        ((int)result1).Should().Be(5);
        ((int)result2).Should().Be(8);
        finalValue.ToString().Should().Be("8");
    }

    #endregion

    #region Redis Hash Operations Tests (Requirement 7.13)

    /// <summary>
    /// Tests that Redis hash operations work correctly for storing structured data.
    /// **Validates: Requirement 7.13 - Redis Hash Operations**
    /// </summary>
    [Fact]
    public async Task Redis_HashOperations_StoreAndRetrieveFields()
    {
        if (_redis is null) return;

        // Arrange
        var db = _redis.GetDatabase();
        var hashKey = $"hash-test-{Guid.NewGuid():N}";

        // Act - Store multiple fields
        await db.HashSetAsync(hashKey, [
            new HashEntry("field1", "value1"),
            new HashEntry("field2", "value2"),
            new HashEntry("counter", "42")
        ]);

        // Retrieve individual field
        var field1 = await db.HashGetAsync(hashKey, "field1");
        
        // Retrieve all fields
        var allFields = await db.HashGetAllAsync(hashKey);
        
        // Increment counter
        var newCounter = await db.HashIncrementAsync(hashKey, "counter", 8);

        // Assert
        field1.ToString().Should().Be("value1");
        allFields.Should().HaveCount(3);
        newCounter.Should().Be(50);
    }

    /// <summary>
    /// Tests that Redis hash field existence checks work correctly.
    /// **Validates: Requirement 7.13 - Redis Hash Operations**
    /// </summary>
    [Fact]
    public async Task Redis_HashOperations_FieldExistenceCheck()
    {
        if (_redis is null) return;

        // Arrange
        var db = _redis.GetDatabase();
        var hashKey = $"hash-exists-{Guid.NewGuid():N}";
        await db.HashSetAsync(hashKey, "existing", "value");

        // Act
        var existingFieldExists = await db.HashExistsAsync(hashKey, "existing");
        var nonExistingFieldExists = await db.HashExistsAsync(hashKey, "nonexisting");

        // Assert
        existingFieldExists.Should().BeTrue();
        nonExistingFieldExists.Should().BeFalse();
    }

    #endregion

    #region Redis Sorted Set Tests

    /// <summary>
    /// Tests that Redis sorted sets maintain ordering by score.
    /// **Validates: Requirement 7.13 - Redis Data Structures**
    /// </summary>
    [Fact]
    public async Task Redis_SortedSet_MaintainsOrderByScore()
    {
        if (_redis is null) return;

        // Arrange
        var db = _redis.GetDatabase();
        var setKey = $"zset-test-{Guid.NewGuid():N}";

        // Act - Add members with scores
        await db.SortedSetAddAsync(setKey, [
            new SortedSetEntry("member3", 30),
            new SortedSetEntry("member1", 10),
            new SortedSetEntry("member2", 20)
        ]);

        // Get members in order
        var members = await db.SortedSetRangeByRankAsync(setKey);

        // Assert - Should be ordered by score
        members.Should().HaveCount(3);
        members[0].ToString().Should().Be("member1");
        members[1].ToString().Should().Be("member2");
        members[2].ToString().Should().Be("member3");
    }

    /// <summary>
    /// Tests that Redis sorted sets support range queries by score.
    /// **Validates: Requirement 7.13 - Redis Data Structures**
    /// </summary>
    [Fact]
    public async Task Redis_SortedSet_RangeByScore()
    {
        if (_redis is null) return;

        // Arrange
        var db = _redis.GetDatabase();
        var setKey = $"zset-range-{Guid.NewGuid():N}";
        
        await db.SortedSetAddAsync(setKey, [
            new SortedSetEntry("low", 10),
            new SortedSetEntry("mid", 50),
            new SortedSetEntry("high", 100)
        ]);

        // Act - Get members with score between 20 and 80
        var members = await db.SortedSetRangeByScoreAsync(setKey, 20, 80);

        // Assert
        members.Should().HaveCount(1);
        members[0].ToString().Should().Be("mid");
    }

    #endregion

    #region Helper Methods

    private static bool IsDockerRunning()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
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
        catch { return false; }
    }

    #endregion
}

#region Test Models

[MemoryPack.MemoryPackable]
public partial class RedisTestEvent : IEvent
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Name { get; set; } = string.Empty;
}

#endregion
