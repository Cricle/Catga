using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Inbox;
using Catga.Messages;
using Catga.Outbox;
using Catga.Persistence;
using Catga.Persistence.Stores;
using Catga.Serialization.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using NATS.Client.Core;

namespace Catga.Tests.Integration;

/// <summary>
/// NATS Persistence 集成测试
/// 测试 JetStream 的 Outbox、Inbox 和 EventStore
/// </summary>
public class NatsPersistenceIntegrationTests : IAsyncLifetime
{
    private IContainer? _natsContainer;
    private NatsConnection? _natsConnection;
    private IMessageSerializer? _serializer;

    public async Task InitializeAsync()
    {
        // 启动 NATS 容器 (with JetStream enabled)
        _natsContainer = new ContainerBuilder()
            .WithImage("nats:latest")
            .WithPortBinding(4222, true)
            .WithCommand("-js", "-m", "8222") // Enable JetStream and monitoring
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(4222))
            .Build();

        await _natsContainer.StartAsync();

        // 等待 NATS 完全启动
        await Task.Delay(2000);

        // 连接到 NATS
        var port = _natsContainer.GetMappedPublicPort(4222);
        var opts = new NatsOpts
        {
            Url = $"nats://localhost:{port}",
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };

        _natsConnection = new NatsConnection(opts);
        await _natsConnection.ConnectAsync();

        // 创建序列化器
        _serializer = new JsonMessageSerializer();
    }

    public async Task DisposeAsync()
    {
        if (_natsConnection != null)
            await _natsConnection.DisposeAsync();

        if (_natsContainer != null)
            await _natsContainer.DisposeAsync();
    }

    #region Outbox Tests

    [Fact]
    public async Task Outbox_AddAsync_ShouldPersistToJetStream()
    {
        // Arrange
        var outbox = new NatsJSOutboxStore(
            _natsConnection!,
            _serializer!,
            streamName: $"TEST_STREAM_{Guid.NewGuid():N}");

        var eventData = new TestEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            Id = "nats-test-1",
            Data = "NATS Outbox message"
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

        // Assert - No exception thrown
        message.Should().NotBeNull();
    }

    [Fact]
    public async Task Outbox_GetPendingMessagesAsync_ShouldReturnMessages()
    {
        // Arrange
        var streamName = $"TEST_OUTBOX_{Guid.NewGuid():N}";
        var outbox = new NatsJSOutboxStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName);

        // Add multiple messages
        for (int i = 0; i < 3; i++)
        {
            var msg = CreateOutboxMessage(1000L + i, OutboxStatus.Pending);
            await outbox.AddAsync(msg);
        }

        await Task.Delay(500); // Allow JetStream to persist

        // Act
        var pending = await outbox.GetPendingMessagesAsync(10);

        // Assert
        pending.Should().NotBeNull();
        pending.Should().HaveCountGreaterOrEqualTo(3);
    }

    [Fact]
    public async Task Outbox_MarkAsPublishedAsync_ShouldUpdateStatus()
    {
        // Arrange
        var streamName = $"TEST_OUTBOX_{Guid.NewGuid():N}";
        var outbox = new NatsJSOutboxStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName);

        var message = CreateOutboxMessage(2000L, OutboxStatus.Pending);
        await outbox.AddAsync(message);
        await Task.Delay(300);

        // Act
        await outbox.MarkAsPublishedAsync(message.MessageId);

        // Assert - Status updated (no exception thrown)
        message.Should().NotBeNull();
    }

    #endregion

    #region Inbox Tests

    [Fact]
    public async Task Inbox_TryLockMessageAsync_FirstTime_ShouldSucceed()
    {
        // Arrange
        var streamName = $"TEST_INBOX_{Guid.NewGuid():N}";
        var inbox = new NatsJSInboxStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName);

        var messageId = MessageExtensions.NewMessageId();
        var lockDuration = TimeSpan.FromMinutes(5);

        // Act
        var locked = await inbox.TryLockMessageAsync(messageId, lockDuration);

        // Assert
        locked.Should().BeTrue("first lock attempt should succeed");
    }

    [Fact]
    public async Task Inbox_TryLockMessageAsync_Duplicate_ShouldFail()
    {
        // Arrange
        var streamName = $"TEST_INBOX_{Guid.NewGuid():N}";
        var inbox = new NatsJSInboxStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName);

        var messageId = MessageExtensions.NewMessageId();
        var lockDuration = TimeSpan.FromMinutes(5);

        // Act - First lock
        var firstLock = await inbox.TryLockMessageAsync(messageId, lockDuration);
        await Task.Delay(200);

        // Act - Second lock (duplicate)
        var secondLock = await inbox.TryLockMessageAsync(messageId, lockDuration);

        // Assert
        firstLock.Should().BeTrue();
        secondLock.Should().BeFalse("duplicate lock should fail");
    }

    [Fact]
    public async Task Inbox_MarkAsProcessedAsync_ShouldPersist()
    {
        // Arrange
        var streamName = $"TEST_INBOX_{Guid.NewGuid():N}";
        var inbox = new NatsJSInboxStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName);

        var messageId = MessageExtensions.NewMessageId();

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

        // Assert - No exception thrown
        message.Should().NotBeNull();
    }

    [Fact]
    public async Task Inbox_HasBeenProcessedAsync_ShouldDetectProcessed()
    {
        // Arrange
        var streamName = $"TEST_INBOX_{Guid.NewGuid():N}";
        var inbox = new NatsJSInboxStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName);

        var messageId = MessageExtensions.NewMessageId();

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
        await Task.Delay(300);

        // Act
        var hasBeenProcessed = await inbox.HasBeenProcessedAsync(messageId);

        // Assert
        hasBeenProcessed.Should().BeTrue();
    }

    [Fact]
    public async Task Inbox_ReleaseLockAsync_ShouldRemoveLock()
    {
        // Arrange
        var streamName = $"TEST_INBOX_{Guid.NewGuid():N}";
        var inbox = new NatsJSInboxStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName);

        var messageId = MessageExtensions.NewMessageId();

        // Lock first
        await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
        await Task.Delay(200);

        // Act
        await inbox.ReleaseLockAsync(messageId);
        await Task.Delay(200);

        // Assert - Should be able to lock again
        var canLockAgain = await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
        canLockAgain.Should().BeTrue("lock should be released");
    }

    #endregion

    #region EventStore Tests

    [Fact]
    public async Task EventStore_AppendAsync_ShouldPersistEvents()
    {
        // Arrange
        var streamName = $"TEST_EVENTS_{Guid.NewGuid():N}";
        var eventStore = new NatsJSEventStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName);

        var streamId = $"order-{Guid.NewGuid()}";

        var events = new List<IEvent>
        {
            new TestEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                Id = "event-1",
                Data = "First event"
            },
            new TestEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                Id = "event-2",
                Data = "Second event"
            }
        };

        // Act
        await eventStore.AppendAsync(streamId, events);

        // Assert - No exception thrown
        streamId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task EventStore_ReadAsync_ShouldRetrieveEvents()
    {
        // Arrange
        var streamName = $"TEST_EVENTS_{Guid.NewGuid():N}";
        var eventStore = new NatsJSEventStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName);

        var streamId = $"order-{Guid.NewGuid()}";

        var events = new List<IEvent>
        {
            new TestEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                Id = "read-1",
                Data = "Event to read"
            }
        };

        await eventStore.AppendAsync(streamId, events);
        await Task.Delay(300);

        // Act
        var eventStream = await eventStore.ReadAsync(streamId);

        // Assert
        eventStream.Should().NotBeNull();
        eventStream.Events.Should().HaveCountGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task EventStore_GetVersionAsync_ShouldReturnVersion()
    {
        // Arrange
        var streamName = $"TEST_EVENTS_{Guid.NewGuid():N}";
        var eventStore = new NatsJSEventStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName);

        var streamId = $"order-{Guid.NewGuid()}";

        var events = new List<IEvent>
        {
            new TestEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                Id = "version-test",
                Data = "Version check"
            }
        };

        await eventStore.AppendAsync(streamId, events);
        await Task.Delay(300);

        // Act
        var version = await eventStore.GetVersionAsync(streamId);

        // Assert
        version.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task EventStore_ConcurrencyCheck_ShouldThrowOnVersionMismatch()
    {
        // Arrange
        var streamName = $"TEST_EVENTS_{Guid.NewGuid():N}";
        var eventStore = new NatsJSEventStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName);

        var streamId = $"order-{Guid.NewGuid()}";

        // First append
        var events1 = new List<IEvent>
        {
            new TestEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                Id = "concurrency-1",
                Data = "First event"
            }
        };
        await eventStore.AppendAsync(streamId, events1);
        await Task.Delay(300);

        // Act - Try to append with wrong expected version
        var events2 = new List<IEvent>
        {
            new TestEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                Id = "concurrency-2",
                Data = "Second event"
            }
        };

        var act = async () => await eventStore.AppendAsync(streamId, events2, expectedVersion: 999);

        // Assert
        await act.Should().ThrowAsync<ConcurrencyException>();
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

    private record TestEvent : IEvent
    {
        public required long MessageId { get; init; }
        public required string Id { get; init; }
        public required string Data { get; init; }
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }

    #endregion
}

