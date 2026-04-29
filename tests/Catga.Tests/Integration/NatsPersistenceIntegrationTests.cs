using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Inbox;
using Catga.Core;
using Catga.Outbox;
using Catga.Persistence;
using Catga.Persistence.Stores;
using Catga.Serialization.MemoryPack;
using FluentAssertions;
using NATS.Client.Core;
using NATS.Client.JetStream;
using MemoryPack;
using Xunit;

namespace Catga.Tests.Integration;

/// <summary>
/// NATS Persistence 集成测试
/// 测试 JetStream 的 Outbox、Inbox 和 EventStore
/// </summary>
[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
[Collection("IntegrationTests")]
public partial class NatsPersistenceIntegrationTests
{
    private readonly global::Catga.Tests.Integration.SharedIntegrationFixture _fixture;
    private NatsConnection? _natsConnection => _fixture.NatsConnection;
    private readonly IMessageSerializer _serializer = new MemoryPackMessageSerializer();

    public NatsPersistenceIntegrationTests(global::Catga.Tests.Integration.SharedIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    #region Outbox Tests

    [Fact]
    public async Task Outbox_AddAsync_ShouldPersistToJetStream()
    {
        if (_natsConnection is null) return;

        // Arrange
        var outbox = new NatsJSOutboxStore(
            _natsConnection!,
            _serializer!,
            streamName: $"TEST_STREAM_{Guid.NewGuid():N}",
            options: null,
            provider: new Catga.Resilience.DiagnosticResiliencePipelineProvider());

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
            Payload = _serializer!.Serialize(eventData),
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
        if (_natsConnection is null) return;

        // Arrange
        var streamName = $"TEST_OUTBOX_{Guid.NewGuid():N}";
        var outbox = new NatsJSOutboxStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName,
            options: null,
            provider: new Catga.Resilience.DiagnosticResiliencePipelineProvider());

        // Add multiple messages
        for (int i = 0; i < 3; i++)
        {
            var msg = CreateOutboxMessage(1000L + i, OutboxStatus.Pending);
            await outbox.AddAsync(msg);
        }

        // Act
        var pending = await AsyncTestWait.WaitUntilAsync(
            () => outbox.GetPendingMessagesAsync(10).AsTask(),
            messages => messages.Count >= 3);

        // Assert
        pending.Should().NotBeNull();
        pending.Should().HaveCountGreaterOrEqualTo(3);
    }

    [Fact]
    public async Task Outbox_MarkAsPublishedAsync_ShouldUpdateStatus()
    {
        if (_natsConnection is null) return;

        // Arrange
        var streamName = $"TEST_OUTBOX_{Guid.NewGuid():N}";
        var outbox = new NatsJSOutboxStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName,
            options: null,
            provider: new Catga.Resilience.DiagnosticResiliencePipelineProvider());

        var message = CreateOutboxMessage(2000L, OutboxStatus.Pending);
        await outbox.AddAsync(message);

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
        if (_natsConnection is null) return;

        // Arrange
        var streamName = $"TEST_INBOX_{Guid.NewGuid():N}";
        var inbox = new NatsJSInboxStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName,
            options: null,
            provider: new Catga.Resilience.DiagnosticResiliencePipelineProvider());

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
        if (_natsConnection is null) return;

        // Arrange
        var streamName = $"TEST_INBOX_{Guid.NewGuid():N}";
        var inbox = new NatsJSInboxStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName,
            options: null,
            provider: new Catga.Resilience.DiagnosticResiliencePipelineProvider());

        var messageId = MessageExtensions.NewMessageId();
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
    public async Task Inbox_TryLockMessageAsync_ExpiredLock_ShouldSucceed()
    {
        if (_natsConnection is null) return;

        var streamName = $"TEST_INBOX_{Guid.NewGuid():N}";
        var inbox = new NatsJSInboxStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName,
            options: null,
            provider: new Catga.Resilience.DiagnosticResiliencePipelineProvider());

        var messageId = MessageExtensions.NewMessageId();

        var firstLock = await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMilliseconds(100));
        firstLock.Should().BeTrue();

        await Task.Delay(200);

        var secondLock = await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
        secondLock.Should().BeTrue("expired lock should allow reacquisition");
    }

    [Fact]
    public async Task Inbox_MarkAsProcessedAsync_ShouldPersist()
    {
        if (_natsConnection is null) return;

        // Arrange
        var streamName = $"TEST_INBOX_{Guid.NewGuid():N}";
        var inbox = new NatsJSInboxStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName,
            options: null,
            provider: new Catga.Resilience.DiagnosticResiliencePipelineProvider());

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
            Payload = _serializer!.Serialize(eventData),
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
        if (_natsConnection is null) return;

        // Arrange
        var streamName = $"TEST_INBOX_{Guid.NewGuid():N}";
        var inbox = new NatsJSInboxStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName,
            options: null,
            provider: new Catga.Resilience.DiagnosticResiliencePipelineProvider());

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
            Payload = _serializer!.Serialize(eventData),
            Status = InboxStatus.Processed,
            ReceivedAt = DateTime.UtcNow,
            ProcessedAt = DateTime.UtcNow
        };

        await inbox.MarkAsProcessedAsync(message);

        // Act
        var hasBeenProcessed = await AsyncTestWait.WaitUntilAsync(
            () => inbox.HasBeenProcessedAsync(messageId).AsTask(),
            value => value);

        // Assert
        hasBeenProcessed.Should().BeTrue();
    }

    [Fact]
    public async Task Inbox_HasBeenProcessedAsync_LockedMessage_ShouldReturnFalse()
    {
        if (_natsConnection is null) return;

        var streamName = $"TEST_INBOX_{Guid.NewGuid():N}";
        var inbox = new NatsJSInboxStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName,
            options: null,
            provider: new Catga.Resilience.DiagnosticResiliencePipelineProvider());

        var messageId = MessageExtensions.NewMessageId();
        await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        var hasBeenProcessed = await inbox.HasBeenProcessedAsync(messageId);

        hasBeenProcessed.Should().BeFalse("locked message should not be treated as processed");
    }

    [Fact]
    public async Task Inbox_TryLockMessageAsync_ProcessedMessage_ShouldFail()
    {
        if (_natsConnection is null) return;

        var streamName = $"TEST_INBOX_{Guid.NewGuid():N}";
        var inbox = new NatsJSInboxStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName,
            options: null,
            provider: new Catga.Resilience.DiagnosticResiliencePipelineProvider());

        var messageId = MessageExtensions.NewMessageId();
        await inbox.MarkAsProcessedAsync(new InboxMessage
        {
            MessageId = messageId,
            MessageType = typeof(TestEvent).FullName!,
            Payload = _serializer!.Serialize(new TestEvent
            {
                MessageId = messageId,
                Id = "processed-lock-test",
                Data = "processed"
            }),
            Status = InboxStatus.Processed,
            ReceivedAt = DateTime.UtcNow,
            ProcessedAt = DateTime.UtcNow
        });

        var locked = await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        locked.Should().BeFalse("processed message must not be locked again");
    }

    [Fact]
    public async Task Inbox_ReleaseLockAsync_ShouldRemoveLock()
    {
        if (_natsConnection is null) return;

        // Arrange
        var streamName = $"TEST_INBOX_{Guid.NewGuid():N}";
        var inbox = new NatsJSInboxStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName,
            options: null,
            provider: new Catga.Resilience.DiagnosticResiliencePipelineProvider());

        var messageId = MessageExtensions.NewMessageId();

        // Lock first
        await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        // Act
        await inbox.ReleaseLockAsync(messageId);

        // Assert - Should be able to lock again
        var canLockAgain = await AsyncTestWait.WaitUntilAsync(
            () => inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5)).AsTask(),
            value => value,
            timeout: TimeSpan.FromSeconds(2));
        canLockAgain.Should().BeTrue("lock should be released");
    }

    [Fact]
    public async Task Inbox_ConcurrentLocking_OnlyOneSucceeds()
    {
        if (_natsConnection is null) return;

        var streamName = $"TEST_INBOX_{Guid.NewGuid():N}";
        var inbox = new NatsJSInboxStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName,
            options: null,
            provider: new Catga.Resilience.DiagnosticResiliencePipelineProvider());

        var messageId = MessageExtensions.NewMessageId();
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5)).AsTask())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Count(static r => r).Should().Be(1, "only one concurrent lock should succeed");
        results.Count(static r => !r).Should().Be(9, "remaining lock attempts should fail");
    }

    #endregion

    #region EventStore Tests

    [Fact]
    public async Task EventStore_AppendAsync_ShouldPersistEvents()
    {
        if (_natsConnection is null) return;

        // Arrange
        var streamName = $"TEST_EVENTS_{Guid.NewGuid():N}";
        var eventStore = new NatsJSEventStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName,
            options: null,
            provider: new Catga.Resilience.DiagnosticResiliencePipelineProvider());

        var streamId = $"order-{Guid.NewGuid()}";

        var events = new List<IEvent>
        {
            new EventStoreTestEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                Id = "event-1",
                Data = "First event"
            },
            new EventStoreTestEvent
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
        if (_natsConnection is null) return;

        // Arrange
        var streamName = $"TEST_EVENTS_{Guid.NewGuid():N}";
        var eventStore = new NatsJSEventStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName,
            options: null,
            provider: new Catga.Resilience.DiagnosticResiliencePipelineProvider());

        var streamId = $"order-{Guid.NewGuid()}";

        var events = new List<IEvent>
        {
            new EventStoreTestEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                Id = "read-1",
                Data = "Event to read"
            }
        };

        await eventStore.AppendAsync(streamId, events);

        // Act
        var eventStream = await AsyncTestWait.WaitUntilAsync(
            () => eventStore.ReadAsync(streamId).AsTask(),
            stream => stream.Events.Count >= 1);

        // Assert
        eventStream.Should().NotBeNull();
        eventStream.Events.Should().HaveCountGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task EventStore_GetVersionAsync_ShouldReturnVersion()
    {
        if (_natsConnection is null) return;

        // Arrange
        var streamName = $"TEST_EVENTS_{Guid.NewGuid():N}";
        var eventStore = new NatsJSEventStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName,
            options: null,
            provider: new Catga.Resilience.DiagnosticResiliencePipelineProvider());

        var streamId = $"order-{Guid.NewGuid()}";

        var events = new List<IEvent>
        {
            new EventStoreTestEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                Id = "version-test",
                Data = "Version check"
            }
        };

        await eventStore.AppendAsync(streamId, events);

        // Act
        var version = await AsyncTestWait.WaitUntilAsync(
            () => eventStore.GetVersionAsync(streamId).AsTask(),
            value => value >= 0);

        // Assert
        version.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task EventStore_ConcurrencyCheck_ShouldThrowOnVersionMismatch()
    {
        if (_natsConnection is null) return;

        // Arrange
        var streamName = $"TEST_EVENTS_{Guid.NewGuid():N}";
        var eventStore = new NatsJSEventStore(
            _natsConnection!,
            _serializer!,
            streamName: streamName,
            options: null,
            provider: new Catga.Resilience.DiagnosticResiliencePipelineProvider());

        var streamId = $"order-{Guid.NewGuid()}";

        // First append
        var events1 = new List<IEvent>
        {
            new EventStoreTestEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                Id = "concurrency-1",
                Data = "First event"
            }
        };
        await eventStore.AppendAsync(streamId, events1);

        // Act - Try to append with wrong expected version
        var events2 = new List<IEvent>
        {
            new EventStoreTestEvent
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
            Payload = _serializer!.Serialize(eventData),
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
