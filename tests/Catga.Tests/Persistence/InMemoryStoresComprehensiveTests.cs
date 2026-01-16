using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Outbox;
using Catga.Persistence.InMemory.Stores;
using Catga.Resilience;
using FluentAssertions;
using Moq;
using Xunit;

namespace Catga.Tests.Persistence;

/// <summary>
/// Tests for MemoryOutboxStore.
/// </summary>
public class MemoryOutboxStoreTests
{
    private readonly IResiliencePipelineProvider _provider;

    public MemoryOutboxStoreTests()
    {
        _provider = new DefaultResiliencePipelineProvider();
    }

    [Fact]
    public async Task AddAsync_ShouldAddMessage()
    {
        var store = new MemoryOutboxStore(_provider);
        var message = new OutboxMessage
        {
            MessageId = 1,
            MessageType = "TestType",
            Payload = new byte[] { 1, 2, 3 }
        };

        await store.AddAsync(message);

        var pending = await store.GetPendingMessagesAsync(10);
        pending.Should().ContainSingle();
    }

    [Fact]
    public async Task GetPendingMessagesAsync_ShouldReturnPendingMessages()
    {
        var store = new MemoryOutboxStore(_provider);

        for (int i = 1; i <= 5; i++)
        {
            await store.AddAsync(new OutboxMessage
            {
                MessageId = i,
                MessageType = "Type",
                Payload = new byte[] { (byte)i }
            });
        }

        var pending = await store.GetPendingMessagesAsync(10);
        pending.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetPendingMessagesAsync_WithLimit_ShouldRespectLimit()
    {
        var store = new MemoryOutboxStore(_provider);

        for (int i = 1; i <= 10; i++)
        {
            await store.AddAsync(new OutboxMessage
            {
                MessageId = i,
                MessageType = "Type",
                Payload = new byte[] { (byte)i }
            });
        }

        var pending = await store.GetPendingMessagesAsync(3);
        pending.Should().HaveCount(3);
    }

    [Fact]
    public async Task MarkAsPublishedAsync_ShouldMarkAsPublished()
    {
        var store = new MemoryOutboxStore(_provider);
        await store.AddAsync(new OutboxMessage
        {
            MessageId = 1,
            MessageType = "Type",
            Payload = new byte[] { 1 }
        });

        await store.MarkAsPublishedAsync(1);

        var pending = await store.GetPendingMessagesAsync(10);
        pending.Should().BeEmpty();
    }

    [Fact]
    public async Task MarkAsFailedAsync_ShouldIncrementRetryCount()
    {
        var store = new MemoryOutboxStore(_provider);
        await store.AddAsync(new OutboxMessage
        {
            MessageId = 1,
            MessageType = "Type",
            Payload = new byte[] { 1 }
        });

        await store.MarkAsFailedAsync(1, "Test error");

        var pending = await store.GetPendingMessagesAsync(10);
        pending.Should().ContainSingle();
        pending.First().RetryCount.Should().Be(1);
    }

    [Fact]
    public async Task MarkAsFailedAsync_MultipleTimes_ShouldIncrementRetryCount()
    {
        var store = new MemoryOutboxStore(_provider);
        await store.AddAsync(new OutboxMessage
        {
            MessageId = 1,
            MessageType = "Type",
            Payload = new byte[] { 1 },
            MaxRetries = 10
        });

        await store.MarkAsFailedAsync(1, "Error 1");
        await store.MarkAsFailedAsync(1, "Error 2");
        await store.MarkAsFailedAsync(1, "Error 3");

        var pending = await store.GetPendingMessagesAsync(10);
        pending.First().RetryCount.Should().Be(3);
    }

    [Fact]
    public async Task GetMessageCountByStatus_ShouldReturnCorrectCount()
    {
        var store = new MemoryOutboxStore(_provider);
        await store.AddAsync(new OutboxMessage { MessageId = 1, MessageType = "Type", Payload = new byte[] { 1 } });
        await store.AddAsync(new OutboxMessage { MessageId = 2, MessageType = "Type", Payload = new byte[] { 2 } });
        await store.AddAsync(new OutboxMessage { MessageId = 3, MessageType = "Type", Payload = new byte[] { 3 } });
        await store.MarkAsPublishedAsync(1);

        var pendingCount = store.GetMessageCountByStatus(OutboxStatus.Pending);
        var publishedCount = store.GetMessageCountByStatus(OutboxStatus.Published);

        pendingCount.Should().Be(2);
        publishedCount.Should().Be(1);
    }
}

/// <summary>
/// Tests for InMemoryIdempotencyStore.
/// </summary>
public class InMemoryIdempotencyStoreExtendedTests
{
    private readonly Mock<IMessageSerializer> _serializerMock;

    public InMemoryIdempotencyStoreExtendedTests()
    {
        _serializerMock = new Mock<IMessageSerializer>();
        _serializerMock.Setup(x => x.Serialize(It.IsAny<object>(), It.IsAny<Type>()))
            .Returns(new byte[] { 1, 2, 3 });
        _serializerMock.Setup(x => x.Deserialize(It.IsAny<byte[]>(), It.IsAny<Type>()))
            .Returns("cached");
    }

    [Fact]
    public async Task HasBeenProcessedAsync_NewKey_ShouldReturnFalse()
    {
        var store = new InMemoryIdempotencyStore(_serializerMock.Object);
        var result = await store.HasBeenProcessedAsync(1);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task MarkAsProcessedAsync_ShouldStoreValue()
    {
        var store = new InMemoryIdempotencyStore(_serializerMock.Object);
        await store.MarkAsProcessedAsync(1, "result");

        var exists = await store.HasBeenProcessedAsync(1);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task GetCachedResultAsync_ShouldReturnStoredValue()
    {
        var store = new InMemoryIdempotencyStore(_serializerMock.Object);
        await store.MarkAsProcessedAsync(1, "result");

        var result = await store.GetCachedResultAsync<string>(1);
        result.Should().Be("cached");
    }

    [Fact]
    public async Task GetCachedResultAsync_NonExistent_ShouldReturnDefault()
    {
        var store = new InMemoryIdempotencyStore(_serializerMock.Object);
        var result = await store.GetCachedResultAsync<string>(999);
        result.Should().BeNull();
    }
}

/// <summary>
/// Tests for InMemoryProjectionCheckpointStore.
/// </summary>
public class InMemoryProjectionCheckpointStoreTests
{
    [Fact]
    public async Task LoadAsync_NewProjection_ShouldReturnNull()
    {
        var store = new InMemoryProjectionCheckpointStore();
        var checkpoint = await store.LoadAsync("test-projection");
        checkpoint.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_ShouldSaveCheckpoint()
    {
        var store = new InMemoryProjectionCheckpointStore();
        var checkpoint = new ProjectionCheckpoint
        {
            ProjectionName = "test-projection",
            Position = 100,
            LastUpdated = DateTime.UtcNow
        };
        await store.SaveAsync(checkpoint);

        var loaded = await store.LoadAsync("test-projection");
        loaded.Should().NotBeNull();
        loaded!.Position.Should().Be(100);
    }

    [Fact]
    public async Task SaveAsync_ShouldUpdateExisting()
    {
        var store = new InMemoryProjectionCheckpointStore();
        await store.SaveAsync(new ProjectionCheckpoint
        {
            ProjectionName = "test-projection",
            Position = 50,
            LastUpdated = DateTime.UtcNow
        });
        await store.SaveAsync(new ProjectionCheckpoint
        {
            ProjectionName = "test-projection",
            Position = 100,
            LastUpdated = DateTime.UtcNow
        });

        var loaded = await store.LoadAsync("test-projection");
        loaded!.Position.Should().Be(100);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveCheckpoint()
    {
        var store = new InMemoryProjectionCheckpointStore();
        await store.SaveAsync(new ProjectionCheckpoint
        {
            ProjectionName = "test-projection",
            Position = 100,
            LastUpdated = DateTime.UtcNow
        });

        await store.DeleteAsync("test-projection");

        var loaded = await store.LoadAsync("test-projection");
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task MultipleProjections_ShouldBeIndependent()
    {
        var store = new InMemoryProjectionCheckpointStore();
        await store.SaveAsync(new ProjectionCheckpoint
        {
            ProjectionName = "projection-1",
            Position = 100,
            LastUpdated = DateTime.UtcNow
        });
        await store.SaveAsync(new ProjectionCheckpoint
        {
            ProjectionName = "projection-2",
            Position = 200,
            LastUpdated = DateTime.UtcNow
        });

        var cp1 = await store.LoadAsync("projection-1");
        var cp2 = await store.LoadAsync("projection-2");

        cp1!.Position.Should().Be(100);
        cp2!.Position.Should().Be(200);
    }

    [Fact]
    public async Task Clear_ShouldRemoveAllCheckpoints()
    {
        var store = new InMemoryProjectionCheckpointStore();
        await store.SaveAsync(new ProjectionCheckpoint
        {
            ProjectionName = "test",
            Position = 100,
            LastUpdated = DateTime.UtcNow
        });

        store.Clear();

        var loaded = await store.LoadAsync("test");
        loaded.Should().BeNull();
    }
}

/// <summary>
/// Tests for InMemorySubscriptionStore.
/// </summary>
public class InMemorySubscriptionStoreExtendedTests
{
    [Fact]
    public async Task LoadAsync_NewSubscription_ShouldReturnNull()
    {
        var store = new InMemorySubscriptionStore();
        var subscription = await store.LoadAsync("sub-1");
        subscription.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_ShouldSaveSubscription()
    {
        var store = new InMemorySubscriptionStore();
        var subscription = new PersistentSubscription("sub-1", "*")
        {
            Position = 50
        };
        await store.SaveAsync(subscription);

        var loaded = await store.LoadAsync("sub-1");
        loaded.Should().NotBeNull();
        loaded!.Position.Should().Be(50);
    }

    [Fact]
    public async Task SaveAsync_ShouldUpdateExisting()
    {
        var store = new InMemorySubscriptionStore();
        await store.SaveAsync(new PersistentSubscription("sub-1", "*") { Position = 50 });
        await store.SaveAsync(new PersistentSubscription("sub-1", "*") { Position = 100 });

        var loaded = await store.LoadAsync("sub-1");
        loaded!.Position.Should().Be(100);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveSubscription()
    {
        var store = new InMemorySubscriptionStore();
        await store.SaveAsync(new PersistentSubscription("sub-1", "*"));

        await store.DeleteAsync("sub-1");

        var loaded = await store.LoadAsync("sub-1");
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_ShouldReturnAllSubscriptions()
    {
        var store = new InMemorySubscriptionStore();
        await store.SaveAsync(new PersistentSubscription("sub-1", "*"));
        await store.SaveAsync(new PersistentSubscription("sub-2", "*"));

        var list = await store.ListAsync();
        list.Should().HaveCount(2);
    }

    [Fact]
    public async Task TryAcquireLockAsync_ShouldSucceedFirstTime()
    {
        var store = new InMemorySubscriptionStore();
        var result = await store.TryAcquireLockAsync("sub-1", "consumer-1");
        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireLockAsync_ShouldFailIfAlreadyLocked()
    {
        var store = new InMemorySubscriptionStore();
        await store.TryAcquireLockAsync("sub-1", "consumer-1");

        var result = await store.TryAcquireLockAsync("sub-1", "consumer-2");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ReleaseLockAsync_ShouldAllowReacquire()
    {
        var store = new InMemorySubscriptionStore();
        await store.TryAcquireLockAsync("sub-1", "consumer-1");
        await store.ReleaseLockAsync("sub-1", "consumer-1");

        var result = await store.TryAcquireLockAsync("sub-1", "consumer-2");
        result.Should().BeTrue();
    }

    [Fact]
    public async Task Clear_ShouldRemoveAllSubscriptions()
    {
        var store = new InMemorySubscriptionStore();
        await store.SaveAsync(new PersistentSubscription("sub-1", "*"));

        store.Clear();

        var loaded = await store.LoadAsync("sub-1");
        loaded.Should().BeNull();
    }
}
