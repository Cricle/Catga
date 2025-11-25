using Catga.Abstractions;
using Catga.Core;
using Catga.Inbox;
using Catga.Outbox;
using Catga.Persistence.Redis.Persistence;
using Catga.Serialization.MemoryPack;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Catga.Resilience;
using MemoryPack;
using Moq;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Catga.Tests.Integration;

/// <summary>
/// Redis Persistence 集成测试
/// 测试 Outbox 和 Inbox 持久化存储
/// </summary>
[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
public partial class RedisPersistenceIntegrationTests : IAsyncLifetime
{
    private RedisContainer? _redisContainer;
    private IConnectionMultiplexer? _redis;
    private IMessageSerializer? _serializer;
    private ILogger<RedisOutboxPersistence>? _outboxLogger;
    private ILogger<RedisInboxPersistence>? _inboxLogger;

    public async Task InitializeAsync()
    {
        // 跳过测试如果 Docker 未运行
        if (!IsDockerRunning())
        {
            // Docker 未运行时，测试会在后续操作时自动失败并跳过
            return;
        }

        // 启动 Redis 容器
        _redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        await _redisContainer.StartAsync();

        // 连接到 Redis
        var connectionString = _redisContainer.GetConnectionString();
        _redis = await ConnectionMultiplexer.ConnectAsync(connectionString);

        // 创建序列化器和日志
        _serializer = new MemoryPackMessageSerializer();
        _outboxLogger = Mock.Of<ILogger<RedisOutboxPersistence>>();
        _inboxLogger = Mock.Of<ILogger<RedisInboxPersistence>>();
    }

    public async Task DisposeAsync()
    {
        _redis?.Dispose();

        if (_redisContainer != null)
            await _redisContainer.DisposeAsync();
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

    #region Outbox Tests

    [Fact]
    public async Task Outbox_AddAsync_ShouldPersistMessage()
    {
        // Arrange
        var outbox = new RedisOutboxPersistence(_redis!, _serializer!, _outboxLogger!, options: null, provider: new DiagnosticResiliencePipelineProvider());
        var eventData = new TestEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            Id = "test-1",
            Data = "Outbox message"
        };

        var message = new OutboxMessage
        {
            MessageId = MessageExtensions.NewMessageId(),
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
        var outbox = new RedisOutboxPersistence(_redis!, _serializer!, _outboxLogger!, options: null, provider: new DiagnosticResiliencePipelineProvider());

        var message1 = CreateOutboxMessage(1001L, OutboxStatus.Pending);
        var message2 = CreateOutboxMessage(1002L, OutboxStatus.Pending);
        var message3 = CreateOutboxMessage(2001L, OutboxStatus.Published);

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
        var outbox = new RedisOutboxPersistence(_redis!, _serializer!, _outboxLogger!, options: null, provider: new DiagnosticResiliencePipelineProvider());
        var message = CreateOutboxMessage(3001L, OutboxStatus.Pending);

        await outbox.AddAsync(message);

        // Act
        await outbox.MarkAsPublishedAsync(message.MessageId);

        // Assert - Verify status changed and removed from pending set
        var db = _redis!.GetDatabase();
        var pendingSetKey = "outbox:pending";
        var inPendingSet = await db.SortedSetScoreAsync(pendingSetKey, message.MessageId);
        inPendingSet.HasValue.Should().BeFalse("message should be removed from pending set");
    }


    [Fact]
    public async Task Outbox_BatchOperations_ShouldHandleMultipleMessages()
    {
        // Arrange
        var outbox = new RedisOutboxPersistence(_redis!, _serializer!, _outboxLogger!, options: null, provider: new DiagnosticResiliencePipelineProvider());
        var messages = Enumerable.Range(1, 20).Select(i =>
            CreateOutboxMessage(4000L + i, OutboxStatus.Pending)
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
        var inbox = new RedisInboxPersistence(_redis!, _serializer!, _inboxLogger!, options: null, provider: new DiagnosticResiliencePipelineProvider());
        var MessageId = MessageExtensions.NewMessageId();
        var lockDuration = TimeSpan.FromMinutes(5);

        // Act
        var locked = await inbox.TryLockMessageAsync(MessageId, lockDuration);

        // Assert
        locked.Should().BeTrue("first lock attempt should succeed");

        // Verify in Redis
        var db = _redis!.GetDatabase();
        var key = $"inbox:msg:{MessageId}";
        var exists = await db.KeyExistsAsync(key);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task Inbox_TryLockMessageAsync_Duplicate_ShouldFail()
    {
        // Arrange
        var inbox = new RedisInboxPersistence(_redis!, _serializer!, _inboxLogger!, options: null, provider: new DiagnosticResiliencePipelineProvider());
        var MessageId = MessageExtensions.NewMessageId();
        var lockDuration = TimeSpan.FromMinutes(5);

        // Act - First lock
        var firstLock = await inbox.TryLockMessageAsync(MessageId, lockDuration);

        // Act - Second lock (duplicate)
        var secondLock = await inbox.TryLockMessageAsync(MessageId, lockDuration);

        // Assert
        firstLock.Should().BeTrue();
        secondLock.Should().BeFalse("duplicate lock should fail");
    }

    [Fact]
    public async Task Inbox_MarkAsProcessedAsync_ShouldUpdateMessage()
    {
        // Arrange
        var inbox = new RedisInboxPersistence(_redis!, _serializer!, _inboxLogger!, options: null, provider: new DiagnosticResiliencePipelineProvider());
        var MessageId = MessageExtensions.NewMessageId();

        // Lock first
        await inbox.TryLockMessageAsync(MessageId, TimeSpan.FromMinutes(5));

        var eventData = new TestEvent
        {
            MessageId = MessageId,
            Id = "inbox-test",
            Data = "Test data"
        };

        var message = new InboxMessage
        {
            MessageId = MessageId,
            MessageType = typeof(TestEvent).FullName!,
            Payload = System.Text.Encoding.UTF8.GetString(_serializer!.Serialize(eventData)),
            Status = InboxStatus.Processing,
            ReceivedAt = DateTime.UtcNow
        };

        // Act
        await inbox.MarkAsProcessedAsync(message);

        // Assert - Message should persist with 24h TTL
        var db = _redis!.GetDatabase();
        var key = $"inbox:msg:{MessageId}";
        var ttl = await db.KeyTimeToLiveAsync(key);
        ttl.Should().NotBeNull();
        ttl.Value.Should().BeCloseTo(TimeSpan.FromHours(24), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Inbox_HasBeenProcessedAsync_ShouldDetectProcessed()
    {
        // Arrange
        var inbox = new RedisInboxPersistence(_redis!, _serializer!, _inboxLogger!, options: null, provider: new DiagnosticResiliencePipelineProvider());
        var MessageId = MessageExtensions.NewMessageId();

        var eventData = new TestEvent
        {
            MessageId = MessageId,
            Id = "check-test",
            Data = "Test"
        };

        var message = new InboxMessage
        {
            MessageId = MessageId,
            MessageType = typeof(TestEvent).FullName!,
            Payload = System.Text.Encoding.UTF8.GetString(_serializer!.Serialize(eventData)),
            Status = InboxStatus.Processed,
            ReceivedAt = DateTime.UtcNow,
            ProcessedAt = DateTime.UtcNow
        };

        await inbox.MarkAsProcessedAsync(message);

        // Act
        var hasBeenProcessed = await inbox.HasBeenProcessedAsync(MessageId);

        // Assert
        hasBeenProcessed.Should().BeTrue();
    }

    [Fact]
    public async Task Inbox_ReleaseLockAsync_ShouldRemoveLock()
    {
        // Arrange
        var inbox = new RedisInboxPersistence(_redis!, _serializer!, _inboxLogger!, options: null, provider: new DiagnosticResiliencePipelineProvider());
        var MessageId = MessageExtensions.NewMessageId();

        // Lock first
        await inbox.TryLockMessageAsync(MessageId, TimeSpan.FromMinutes(5));

        // Act
        await inbox.ReleaseLockAsync(MessageId);

        // Assert - Should be able to lock again
        var canLockAgain = await inbox.TryLockMessageAsync(MessageId, TimeSpan.FromMinutes(5));
        canLockAgain.Should().BeTrue("lock should be released");
    }

    [Fact]
    public async Task Inbox_ConcurrentLocking_OnlyOneSucceeds()
    {
        // Arrange
        var inbox = new RedisInboxPersistence(_redis!, _serializer!, _inboxLogger!, options: null, provider: new DiagnosticResiliencePipelineProvider());
        var MessageId = MessageExtensions.NewMessageId();
        var lockDuration = TimeSpan.FromMinutes(5);

        // Act - Simulate concurrent lock attempts
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => inbox.TryLockMessageAsync(MessageId, lockDuration).AsTask())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert - Only one should succeed
        results.Count(r => r).Should().Be(1, "only one concurrent lock should succeed");
        results.Count(r => !r).Should().Be(9, "nine should fail");
    }

    #endregion

    #region Helper Methods

    private OutboxMessage CreateOutboxMessage(long messageId, OutboxStatus status)
    {
        var eventData = new TestEvent
        {
            MessageId = messageId,
            Id = messageId.ToString(),
            Data = $"Data for {messageId}"
        };

        return new OutboxMessage
        {
            MessageId = messageId,
            MessageType = typeof(TestEvent).FullName!,
            Payload = System.Text.Encoding.UTF8.GetString(_serializer!.Serialize(eventData)),
            Status = status,
            CreatedAt = DateTime.UtcNow,
            PublishedAt = status == OutboxStatus.Published ? DateTime.UtcNow : null
        };
    }

    #endregion

    #region Test Models

    [MemoryPackable]
    private partial record TestEvent : IEvent
    {
        public required long MessageId { get; init; }
        public required string Id { get; init; }
        public required string Data { get; init; }
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }

    #endregion
}

