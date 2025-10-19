using Catga.Abstractions;
using Catga.Inbox;
using Catga.Messages;
using Catga.Outbox;
using Catga.Persistence.Redis.Persistence;
using Catga.Serialization.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Catga.Tests.Integration;

/// <summary>
/// Redis Persistence 集成测试
/// 测试 Outbox 和 Inbox 持久化存储
/// </summary>
public class RedisPersistenceIntegrationTests : IAsyncLifetime
{
    private RedisContainer? _redisContainer;
    private IConnectionMultiplexer? _redis;
    private IMessageSerializer? _serializer;
    private ILogger<RedisOutboxPersistence>? _outboxLogger;
    private ILogger<RedisInboxPersistence>? _inboxLogger;

    public async Task InitializeAsync()
    {
        // 启动 Redis 容器
        _redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        await _redisContainer.StartAsync();

        // 连接到 Redis
        var connectionString = _redisContainer.GetConnectionString();
        _redis = await ConnectionMultiplexer.ConnectAsync(connectionString);

        // 创建序列化器和日志
        _serializer = new JsonMessageSerializer();
        _outboxLogger = Mock.Of<ILogger<RedisOutboxPersistence>>();
        _inboxLogger = Mock.Of<ILogger<RedisInboxPersistence>>();
    }

    public async Task DisposeAsync()
    {
        _redis?.Dispose();

        if (_redisContainer != null)
            await _redisContainer.DisposeAsync();
    }

    #region Outbox Tests

    [Fact]
    public async Task Outbox_AddAsync_ShouldPersistMessage()
    {
        // Arrange
        var outbox = new RedisOutboxPersistence(_redis!, _serializer!, _outboxLogger!);
        var eventData = new TestEvent
        {
            MessageId = Guid.NewGuid().ToString(),
            Id = "test-1",
            Data = "Outbox message"
        };

        var message = new OutboxMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            MessageType = typeof(TestEvent).FullName!,
            Payload = System.Text.Encoding.UTF8.GetString(_serializer!.Serialize(eventData)),
            Status = OutboxStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await outbox.AddAsync(message);

        // Assert - Verify message was stored in Redis
        var db = _redis!.GetDatabase();
        var key = $"outbox:msg:{message.MessageId}";
        var exists = await db.KeyExistsAsync(key);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task Outbox_GetPendingMessagesAsync_ShouldReturnPendingOnly()
    {
        // Arrange
        var outbox = new RedisOutboxPersistence(_redis!, _serializer!, _outboxLogger!);

        var message1 = CreateOutboxMessage("pending-1", OutboxStatus.Pending);
        var message2 = CreateOutboxMessage("pending-2", OutboxStatus.Pending);
        var message3 = CreateOutboxMessage("published-1", OutboxStatus.Published);

        await outbox.AddAsync(message1);
        await outbox.AddAsync(message2);
        await outbox.AddAsync(message3);

        // Act
        var pending = await outbox.GetPendingMessagesAsync(10);

        // Assert
        pending.Should().HaveCountGreaterOrEqualTo(2);
        pending.Should().AllSatisfy(m => m.Status.Should().Be(OutboxStatus.Pending));
    }

    [Fact]
    public async Task Outbox_MarkAsPublishedAsync_ShouldUpdateStatus()
    {
        // Arrange
        var outbox = new RedisOutboxPersistence(_redis!, _serializer!, _outboxLogger!);
        var message = CreateOutboxMessage("test-msg", OutboxStatus.Pending);

        await outbox.AddAsync(message);

        // Act
        await outbox.MarkAsPublishedAsync(message.MessageId);

        // Assert - Verify status changed and removed from pending set
        var db = _redis!.GetDatabase();
        var pendingSetKey = "outbox:pending";
        var inPendingSet = await db.SortedSetScoreAsync(pendingSetKey, message.MessageId);
        inPendingSet.HasValue.Should().BeFalse("message should be removed from pending set");
    }

    [Fact(Skip = "DeleteAsync method not available in current implementation")]
    public async Task Outbox_DeleteAsync_ShouldRemoveMessage()
    {
        // TODO: Implement DeleteAsync in RedisOutboxPersistence if needed
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Outbox_BatchOperations_ShouldHandleMultipleMessages()
    {
        // Arrange
        var outbox = new RedisOutboxPersistence(_redis!, _serializer!, _outboxLogger!);
        var messages = Enumerable.Range(1, 20).Select(i =>
            CreateOutboxMessage($"batch-{i}", OutboxStatus.Pending)
        ).ToList();

        // Act - Add all messages
        foreach (var msg in messages)
        {
            await outbox.AddAsync(msg);
        }

        // Act - Get pending (batch size 10)
        var pending = await outbox.GetPendingMessagesAsync(10);

        // Assert
        pending.Should().HaveCount(10, "batch size limit should be respected");
        pending.Should().AllSatisfy(m => m.Status.Should().Be(OutboxStatus.Pending));
    }

    #endregion

    #region Inbox Tests

    [Fact]
    public async Task Inbox_TryLockMessageAsync_FirstTime_ShouldSucceed()
    {
        // Arrange
        var inbox = new RedisInboxPersistence(_redis!, _serializer!, _inboxLogger!);
        var messageId = Guid.NewGuid().ToString();
        var lockDuration = TimeSpan.FromMinutes(5);

        // Act
        var locked = await inbox.TryLockMessageAsync(messageId, lockDuration);

        // Assert
        locked.Should().BeTrue("first lock attempt should succeed");

        // Verify in Redis
        var db = _redis!.GetDatabase();
        var key = $"inbox:msg:{messageId}";
        var exists = await db.KeyExistsAsync(key);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task Inbox_TryLockMessageAsync_Duplicate_ShouldFail()
    {
        // Arrange
        var inbox = new RedisInboxPersistence(_redis!, _serializer!, _inboxLogger!);
        var messageId = Guid.NewGuid().ToString();
        var lockDuration = TimeSpan.FromMinutes(5);

        // Act - First lock
        var firstLock = await inbox.TryLockMessageAsync(messageId, lockDuration);

        // Act - Second lock (duplicate)
        var secondLock = await inbox.TryLockMessageAsync(messageId, lockDuration);

        // Assert
        firstLock.Should().BeTrue();
        secondLock.Should().BeFalse("duplicate lock should fail");
    }

    [Fact]
    public async Task Inbox_MarkAsProcessedAsync_ShouldUpdateMessage()
    {
        // Arrange
        var inbox = new RedisInboxPersistence(_redis!, _serializer!, _inboxLogger!);
        var messageId = Guid.NewGuid().ToString();

        // Lock first
        await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        var eventData = new TestEvent
        {
            MessageId = messageId,
            Id = "inbox-test",
            Data = "Test data"
        };

        var message = new InboxMessage
        {
            MessageId = messageId,
            MessageType = typeof(TestEvent).FullName!,
            Payload = System.Text.Encoding.UTF8.GetString(_serializer!.Serialize(eventData)),
            Status = InboxStatus.Processing,
            ReceivedAt = DateTime.UtcNow
        };

        // Act
        await inbox.MarkAsProcessedAsync(message);

        // Assert - Message should persist with 24h TTL
        var db = _redis!.GetDatabase();
        var key = $"inbox:msg:{messageId}";
        var ttl = await db.KeyTimeToLiveAsync(key);
        ttl.Should().NotBeNull();
        ttl.Value.Should().BeCloseTo(TimeSpan.FromHours(24), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Inbox_HasBeenProcessedAsync_ShouldDetectProcessed()
    {
        // Arrange
        var inbox = new RedisInboxPersistence(_redis!, _serializer!, _inboxLogger!);
        var messageId = Guid.NewGuid().ToString();

        var eventData = new TestEvent
        {
            MessageId = messageId,
            Id = "check-test",
            Data = "Test"
        };

        var message = new InboxMessage
        {
            MessageId = messageId,
            MessageType = typeof(TestEvent).FullName!,
            Payload = System.Text.Encoding.UTF8.GetString(_serializer!.Serialize(eventData)),
            Status = InboxStatus.Processed,
            ReceivedAt = DateTime.UtcNow,
            ProcessedAt = DateTime.UtcNow
        };

        await inbox.MarkAsProcessedAsync(message);

        // Act
        var hasBeenProcessed = await inbox.HasBeenProcessedAsync(messageId);

        // Assert
        hasBeenProcessed.Should().BeTrue();
    }

    [Fact]
    public async Task Inbox_ReleaseLockAsync_ShouldRemoveLock()
    {
        // Arrange
        var inbox = new RedisInboxPersistence(_redis!, _serializer!, _inboxLogger!);
        var messageId = Guid.NewGuid().ToString();

        // Lock first
        await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        // Act
        await inbox.ReleaseLockAsync(messageId);

        // Assert - Should be able to lock again
        var canLockAgain = await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
        canLockAgain.Should().BeTrue("lock should be released");
    }

    [Fact]
    public async Task Inbox_ConcurrentLocking_OnlyOneSucceeds()
    {
        // Arrange
        var inbox = new RedisInboxPersistence(_redis!, _serializer!, _inboxLogger!);
        var messageId = Guid.NewGuid().ToString();
        var lockDuration = TimeSpan.FromMinutes(5);

        // Act - Simulate concurrent lock attempts
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => inbox.TryLockMessageAsync(messageId, lockDuration).AsTask())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert - Only one should succeed
        results.Count(r => r).Should().Be(1, "only one concurrent lock should succeed");
        results.Count(r => !r).Should().Be(9, "nine should fail");
    }

    #endregion

    #region Helper Methods

    private OutboxMessage CreateOutboxMessage(string id, OutboxStatus status)
    {
        var eventData = new TestEvent
        {
            MessageId = id,
            Id = id,
            Data = $"Data for {id}"
        };

        return new OutboxMessage
        {
            MessageId = id,
            MessageType = typeof(TestEvent).FullName!,
            Payload = System.Text.Encoding.UTF8.GetString(_serializer!.Serialize(eventData)),
            Status = status,
            CreatedAt = DateTime.UtcNow,
            PublishedAt = status == OutboxStatus.Published ? DateTime.UtcNow : null
        };
    }

    #endregion

    #region Test Models

    private record TestEvent : IEvent
    {
        public required string MessageId { get; init; }
        public required string Id { get; init; }
        public required string Data { get; init; }
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }

    #endregion
}

