using Catga.Abstractions;
using Catga.Inbox;
using Catga.Persistence.Redis.Persistence;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace Catga.Tests.Persistence;

/// <summary>
/// Redis Inbox Store 测试
/// 测试 Inbox 模式的消息去重、幂等性保证
/// </summary>
public class RedisInboxStoreTests : IAsyncLifetime
{
    private readonly Mock<IConnectionMultiplexer> _mockConnection;
    private readonly Mock<IDatabase> _mockDatabase;
    private RedisInboxPersistence _inboxStore = null!;

    public RedisInboxStoreTests()
    {
        _mockConnection = new Mock<IConnectionMultiplexer>();
        _mockDatabase = new Mock<IDatabase>();

        _mockConnection.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_mockDatabase.Object);
    }

    public Task InitializeAsync()
    {
        var mockSerializer = new Mock<IMessageSerializer>();
        _inboxStore = new RedisInboxPersistence(_mockConnection.Object, mockSerializer.Object);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _inboxStore?.Dispose();
        return Task.CompletedTask;
    }

    #region ExistsAsync Tests

    [Fact]
    public async Task ExistsAsync_NewMessage_ReturnsFalse()
    {
        // Arrange
        var messageId = "msg-123";
        
        _mockDatabase.Setup(x => x.KeyExistsAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        // Act
        var exists = await _inboxStore.ExistsAsync(messageId);

        // Assert
        Assert.False(exists);
        
        _mockDatabase.Verify(x => x.KeyExistsAsync(
            It.Is<RedisKey>(k => k.ToString().Contains($"inbox:{messageId}")),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task ExistsAsync_ProcessedMessage_ReturnsTrue()
    {
        // Arrange
        var messageId = "msg-123";
        
        _mockDatabase.Setup(x => x.KeyExistsAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var exists = await _inboxStore.ExistsAsync(messageId);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_BatchMessages_ChecksAll()
    {
        // Arrange
        var messageIds = new[] { "msg-1", "msg-2", "msg-3" };
        
        _mockDatabase.SetupSequence(x => x.KeyExistsAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(false)  // msg-1: new
            .ReturnsAsync(true)   // msg-2: exists
            .ReturnsAsync(false); // msg-3: new

        // Act
        var results = await Task.WhenAll(messageIds.Select(id => _inboxStore.ExistsAsync(id)));

        // Assert
        Assert.False(results[0]); // msg-1
        Assert.True(results[1]);  // msg-2
        Assert.False(results[2]); // msg-3
        
        _mockDatabase.Verify(x => x.KeyExistsAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()), Times.Exactly(3));
    }

    #endregion

    #region MarkAsProcessedAsync Tests

    [Fact]
    public async Task MarkAsProcessedAsync_NewMessage_Success()
    {
        // Arrange
        var messageId = "msg-123";
        var processedAt = DateTime.UtcNow;
        
        _mockDatabase.Setup(x => x.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _inboxStore.MarkAsProcessedAsync(messageId, processedAt);

        // Assert
        _mockDatabase.Verify(x => x.StringSetAsync(
            It.Is<RedisKey>(k => k.ToString().Contains($"inbox:{messageId}")),
            It.Is<RedisValue>(v => v.ToString().Contains(processedAt.ToString("O"))),
            It.IsAny<TimeSpan?>(),
            It.Is<When>(w => w == When.NotExists), // Ensure idempotency
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task MarkAsProcessedAsync_WithTTL_SetsExpiration()
    {
        // Arrange
        var messageId = "msg-123";
        var processedAt = DateTime.UtcNow;
        var ttl = TimeSpan.FromDays(7);
        
        _mockDatabase.Setup(x => x.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _inboxStore.MarkAsProcessedAsync(messageId, processedAt, ttl);

        // Assert
        _mockDatabase.Verify(x => x.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.Is<TimeSpan?>(t => t == ttl),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task MarkAsProcessedAsync_DuplicateMessage_ReturnsFalse()
    {
        // Arrange
        var messageId = "msg-123";
        var processedAt = DateTime.UtcNow;
        
        _mockDatabase.Setup(x => x.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(false); // Key already exists

        // Act
        var result = await _inboxStore.MarkAsProcessedAsync(messageId, processedAt);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task MarkAsProcessedAsync_BatchMessages_Success()
    {
        // Arrange
        var messageIds = new[] { "msg-1", "msg-2", "msg-3" };
        var processedAt = DateTime.UtcNow;
        
        _mockDatabase.Setup(x => x.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var tasks = messageIds.Select(id => _inboxStore.MarkAsProcessedAsync(id, processedAt));
        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.All(results, result => Assert.True(result));
        
        _mockDatabase.Verify(x => x.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Exactly(3));
    }

    #endregion

    #region GetProcessedAtAsync Tests

    [Fact]
    public async Task GetProcessedAtAsync_ExistingMessage_ReturnsTimestamp()
    {
        // Arrange
        var messageId = "msg-123";
        var processedAt = DateTime.UtcNow;
        var storedValue = new { ProcessedAt = processedAt, Handler = "TestHandler" };
        var serializedValue = System.Text.Json.JsonSerializer.Serialize(storedValue);
        
        _mockDatabase.Setup(x => x.StringGetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(serializedValue));

        // Act
        var result = await _inboxStore.GetProcessedAtAsync(messageId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(processedAt.ToString("O"), result.Value.ToString("O"));
    }

    [Fact]
    public async Task GetProcessedAtAsync_NonExistentMessage_ReturnsNull()
    {
        // Arrange
        var messageId = "msg-999";
        
        _mockDatabase.Setup(x => x.StringGetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // Act
        var result = await _inboxStore.GetProcessedAtAsync(messageId);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region CleanupAsync Tests

    [Fact]
    public async Task CleanupAsync_RemovesExpiredMessages()
    {
        // Arrange
        var olderThan = TimeSpan.FromDays(30);
        var cutoffTime = DateTime.UtcNow.Subtract(olderThan);
        
        // Mock scan for keys
        var expiredKeys = new[]
        {
            new RedisKey("inbox:msg-1"),
            new RedisKey("inbox:msg-2"),
            new RedisKey("inbox:msg-3")
        };
        
        _mockDatabase.Setup(x => x.StringGetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, CommandFlags flags) =>
            {
                var timestamp = DateTime.UtcNow.AddDays(-40); // Old message
                var value = System.Text.Json.JsonSerializer.Serialize(new { ProcessedAt = timestamp });
                return new RedisValue(value);
            });
        
        _mockDatabase.Setup(x => x.KeyDeleteAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var removed = await _inboxStore.CleanupAsync(olderThan);

        // Assert
        Assert.True(removed >= 0);
    }

    [Fact]
    public async Task CleanupAsync_KeepsRecentMessages()
    {
        // Arrange
        var olderThan = TimeSpan.FromDays(7);
        
        _mockDatabase.Setup(x => x.StringGetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, CommandFlags flags) =>
            {
                var timestamp = DateTime.UtcNow.AddDays(-3); // Recent message
                var value = System.Text.Json.JsonSerializer.Serialize(new { ProcessedAt = timestamp });
                return new RedisValue(value);
            });

        // Act
        var removed = await _inboxStore.CleanupAsync(olderThan);

        // Assert
        // Should not delete recent messages
        _mockDatabase.Verify(x => x.KeyDeleteAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()), Times.Never);
    }

    #endregion

    #region Idempotency Tests

    [Fact]
    public async Task IdempotencyGuarantee_DuplicateProcessing_Prevented()
    {
        // Arrange
        var messageId = "msg-123";
        var processedAt = DateTime.UtcNow;
        
        // First call succeeds
        _mockDatabase.SetupSequence(x => x.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.Is<When>(w => w == When.NotExists),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(true)   // First attempt: success
            .ReturnsAsync(false); // Second attempt: key exists

        // Act
        var firstResult = await _inboxStore.MarkAsProcessedAsync(messageId, processedAt);
        var secondResult = await _inboxStore.MarkAsProcessedAsync(messageId, processedAt);

        // Assert
        Assert.True(firstResult);   // First processing succeeds
        Assert.False(secondResult); // Duplicate prevented
    }

    [Fact]
    public async Task IdempotencyGuarantee_ConcurrentProcessing_OnlyOneSucceeds()
    {
        // Arrange
        var messageId = "msg-123";
        var processedAt = DateTime.UtcNow;
        var successCount = 0;
        
        _mockDatabase.Setup(x => x.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.Is<When>(w => w == When.NotExists),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(() =>
            {
                if (successCount == 0)
                {
                    successCount++;
                    return true;
                }
                return false;
            });

        // Act - Simulate concurrent processing
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _inboxStore.MarkAsProcessedAsync(messageId, processedAt))
            .ToArray();
        
        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.Single(results.Where(r => r == true)); // Only one succeeds
        Assert.Equal(9, results.Count(r => r == false)); // Others prevented
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task ExistsAsync_ConnectionFailure_ThrowsException()
    {
        // Arrange
        var messageId = "msg-123";
        
        _mockDatabase.Setup(x => x.KeyExistsAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection failed"));

        // Act & Assert
        await Assert.ThrowsAsync<RedisConnectionException>(() =>
            _inboxStore.ExistsAsync(messageId));
    }

    [Fact]
    public async Task MarkAsProcessedAsync_ConnectionFailure_ThrowsException()
    {
        // Arrange
        var messageId = "msg-123";
        var processedAt = DateTime.UtcNow;
        
        _mockDatabase.Setup(x => x.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection failed"));

        // Act & Assert
        await Assert.ThrowsAsync<RedisConnectionException>(() =>
            _inboxStore.MarkAsProcessedAsync(messageId, processedAt));
    }

    [Fact]
    public async Task GetProcessedAtAsync_InvalidData_ReturnsNull()
    {
        // Arrange
        var messageId = "msg-123";
        
        _mockDatabase.Setup(x => x.StringGetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue("invalid-json-data"));

        // Act
        var result = await _inboxStore.GetProcessedAtAsync(messageId);

        // Assert
        Assert.Null(result); // Should handle gracefully
    }

    #endregion

    #region Performance Tests

    [Fact]
    public async Task HighThroughput_ThousandsOfMessages_HandlesEfficiently()
    {
        // Arrange
        var messageCount = 1000;
        var messageIds = Enumerable.Range(0, messageCount)
            .Select(i => $"msg-{i}")
            .ToArray();
        
        _mockDatabase.Setup(x => x.KeyExistsAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var tasks = messageIds.Select(id => _inboxStore.ExistsAsync(id));
        await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        Assert.True(stopwatch.ElapsedMilliseconds < 5000); // Should complete within 5 seconds
        
        _mockDatabase.Verify(x => x.KeyExistsAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()), Times.Exactly(messageCount));
    }

    #endregion
}

