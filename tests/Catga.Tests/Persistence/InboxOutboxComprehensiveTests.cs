using Catga.Abstractions;
using Catga.Inbox;
using Catga.Outbox;
using Catga.Persistence.Redis;
using Catga.Persistence.Stores;
using Catga.Resilience;
using Catga.Serialization.MemoryPack;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NSubstitute;
using StackExchange.Redis;
using Xunit;
using Xunit.Sdk;

namespace Catga.Tests.Persistence;

/// <summary>
/// Comprehensive tests for IInboxStore implementations (InMemory, Redis, NATS)
/// </summary>
public abstract class InboxStoreTestsBase
{
    protected abstract IInboxStore CreateStore();
    protected abstract Task CleanupAsync();

    protected long NextMessageId() => DateTime.UtcNow.Ticks + Random.Shared.Next();

    [SkippableFact]
    public async Task TryLockMessageAsync_NewMessage_ShouldReturnTrue()
    {
        var store = CreateStore();
        var messageId = NextMessageId();

        var result = await store.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        result.Should().BeTrue();
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task TryLockMessageAsync_AlreadyLockedMessage_ShouldReturnFalse()
    {
        var store = CreateStore();
        var messageId = NextMessageId();
        await store.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        var result = await store.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        result.Should().BeFalse();
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task TryLockMessageAsync_ExpiredLock_ShouldReturnTrue()
    {
        var store = CreateStore();
        var messageId = NextMessageId();
        await store.TryLockMessageAsync(messageId, TimeSpan.FromMilliseconds(10));
        await Task.Delay(50);

        var result = await store.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        result.Should().BeTrue();
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task TryLockMessageAsync_ProcessedMessage_ShouldReturnFalse()
    {
        var store = CreateStore();
        var messageId = NextMessageId();
        var message = new InboxMessage { MessageId = messageId, MessageType = "Test", Payload = [1, 2, 3] };
        await store.MarkAsProcessedAsync(message);

        var result = await store.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        result.Should().BeFalse();
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task MarkAsProcessedAsync_ShouldSetStatusToProcessed()
    {
        var store = CreateStore();
        var messageId = NextMessageId();
        var message = new InboxMessage
        {
            MessageId = messageId,
            MessageType = "TestMessage",
            Payload = [1, 2, 3],
            ProcessingResult = [4, 5, 6]
        };

        await store.MarkAsProcessedAsync(message);

        var hasBeenProcessed = await store.HasBeenProcessedAsync(messageId);
        hasBeenProcessed.Should().BeTrue();
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task MarkAsProcessedAsync_WithCorrelationId_ShouldStore()
    {
        var store = CreateStore();
        var messageId = NextMessageId();
        var message = new InboxMessage
        {
            MessageId = messageId,
            MessageType = "TestMessage",
            Payload = [1, 2, 3],
            CorrelationId = 12345L
        };

        await store.MarkAsProcessedAsync(message);

        var hasBeenProcessed = await store.HasBeenProcessedAsync(messageId);
        hasBeenProcessed.Should().BeTrue();
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task MarkAsProcessedAsync_WithMetadata_ShouldStore()
    {
        var store = CreateStore();
        var messageId = NextMessageId();
        var message = new InboxMessage
        {
            MessageId = messageId,
            MessageType = "TestMessage",
            Payload = [1, 2, 3],
            Metadata = "{\"key\":\"value\"}"
        };

        await store.MarkAsProcessedAsync(message);

        var hasBeenProcessed = await store.HasBeenProcessedAsync(messageId);
        hasBeenProcessed.Should().BeTrue();
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task HasBeenProcessedAsync_NewMessage_ShouldReturnFalse()
    {
        var store = CreateStore();
        var messageId = NextMessageId();

        var result = await store.HasBeenProcessedAsync(messageId);

        result.Should().BeFalse();
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task HasBeenProcessedAsync_ProcessedMessage_ShouldReturnTrue()
    {
        var store = CreateStore();
        var messageId = NextMessageId();
        await store.MarkAsProcessedAsync(new InboxMessage { MessageId = messageId, MessageType = "Test", Payload = [] });

        var result = await store.HasBeenProcessedAsync(messageId);

        result.Should().BeTrue();
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task GetProcessedResultAsync_ProcessedMessage_ShouldReturnResult()
    {
        var store = CreateStore();
        var messageId = NextMessageId();
        var expectedResult = new byte[] { 10, 20, 30 };
        await store.MarkAsProcessedAsync(new InboxMessage
        {
            MessageId = messageId,
            MessageType = "Test",
            Payload = [1, 2, 3],
            ProcessingResult = expectedResult
        });

        var result = await store.GetProcessedResultAsync(messageId);

        result.Should().BeEquivalentTo(expectedResult);
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task GetProcessedResultAsync_NewMessage_ShouldReturnNull()
    {
        var store = CreateStore();
        var messageId = NextMessageId();

        var result = await store.GetProcessedResultAsync(messageId);

        result.Should().BeNull();
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task GetProcessedResultAsync_LockedMessage_ShouldReturnNull()
    {
        var store = CreateStore();
        var messageId = NextMessageId();
        await store.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        var result = await store.GetProcessedResultAsync(messageId);

        result.Should().BeNull();
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task ReleaseLockAsync_ShouldAllowRelock()
    {
        var store = CreateStore();
        var messageId = NextMessageId();
        await store.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        await store.ReleaseLockAsync(messageId);
        var result = await store.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        result.Should().BeTrue();
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task ReleaseLockAsync_NonExistentMessage_ShouldNotThrow()
    {
        var store = CreateStore();
        var messageId = NextMessageId();

        var act = async () => await store.ReleaseLockAsync(messageId);

        await act.Should().NotThrowAsync();
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task DeleteProcessedMessagesAsync_ShouldNotThrow()
    {
        var store = CreateStore();

        var act = async () => await store.DeleteProcessedMessagesAsync(TimeSpan.FromDays(1));

        await act.Should().NotThrowAsync();
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task MultipleMessages_ShouldBeIndependent()
    {
        var store = CreateStore();
        var id1 = NextMessageId();
        var id2 = NextMessageId();
        var id3 = NextMessageId();

        await store.MarkAsProcessedAsync(new InboxMessage { MessageId = id1, MessageType = "T1", Payload = [1], ProcessingResult = [10] });
        await store.MarkAsProcessedAsync(new InboxMessage { MessageId = id2, MessageType = "T2", Payload = [2], ProcessingResult = [20] });
        await store.TryLockMessageAsync(id3, TimeSpan.FromMinutes(5));

        (await store.HasBeenProcessedAsync(id1)).Should().BeTrue();
        (await store.HasBeenProcessedAsync(id2)).Should().BeTrue();
        (await store.HasBeenProcessedAsync(id3)).Should().BeFalse();
        (await store.GetProcessedResultAsync(id1)).Should().BeEquivalentTo(new byte[] { 10 });
        (await store.GetProcessedResultAsync(id2)).Should().BeEquivalentTo(new byte[] { 20 });
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task ConcurrentLocks_ShouldBeThreadSafe()
    {
        var store = CreateStore();
        var messageId = NextMessageId();
        var successCount = 0;

        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            var result = await store.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
            if (result) Interlocked.Increment(ref successCount);
        });

        await Task.WhenAll(tasks);

        successCount.Should().Be(1);
        await CleanupAsync();
    }
}

/// <summary>
/// Comprehensive tests for IOutboxStore implementations (InMemory, Redis, NATS)
/// </summary>
public abstract class OutboxStoreTestsBase
{
    protected abstract IOutboxStore CreateStore();
    protected abstract Task CleanupAsync();

    protected long NextMessageId() => DateTime.UtcNow.Ticks + Random.Shared.Next();

    [SkippableFact]
    public async Task AddAsync_ShouldAddMessage()
    {
        var store = CreateStore();
        var message = new OutboxMessage
        {
            MessageId = NextMessageId(),
            MessageType = "TestType",
            Payload = [1, 2, 3]
        };

        await store.AddAsync(message);

        var pending = await store.GetPendingMessagesAsync(10);
        pending.Should().ContainSingle(m => m.MessageId == message.MessageId);
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task AddAsync_WithCorrelationId_ShouldStore()
    {
        var store = CreateStore();
        var message = new OutboxMessage
        {
            MessageId = NextMessageId(),
            MessageType = "TestType",
            Payload = [1, 2, 3],
            CorrelationId = 12345L
        };

        await store.AddAsync(message);

        var pending = await store.GetPendingMessagesAsync(10);
        pending.Should().ContainSingle(m => m.CorrelationId == 12345L);
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task AddAsync_WithMetadata_ShouldStore()
    {
        var store = CreateStore();
        var message = new OutboxMessage
        {
            MessageId = NextMessageId(),
            MessageType = "TestType",
            Payload = [1, 2, 3],
            Metadata = "{\"key\":\"value\"}"
        };

        await store.AddAsync(message);

        var pending = await store.GetPendingMessagesAsync(10);
        pending.Should().ContainSingle(m => m.Metadata == "{\"key\":\"value\"}");
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task AddAsync_NullMessage_ShouldThrow()
    {
        var store = CreateStore();

        var act = async () => await store.AddAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task GetPendingMessagesAsync_ShouldReturnPendingMessages()
    {
        var store = CreateStore();
        for (int i = 0; i < 5; i++)
        {
            await store.AddAsync(new OutboxMessage
            {
                MessageId = NextMessageId(),
                MessageType = "Type",
                Payload = [(byte)i]
            });
        }

        var pending = await store.GetPendingMessagesAsync(10);

        pending.Should().HaveCount(5);
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task GetPendingMessagesAsync_WithLimit_ShouldRespectLimit()
    {
        var store = CreateStore();
        for (int i = 0; i < 10; i++)
        {
            await store.AddAsync(new OutboxMessage
            {
                MessageId = NextMessageId(),
                MessageType = "Type",
                Payload = [(byte)i]
            });
        }

        var pending = await store.GetPendingMessagesAsync(3);

        pending.Should().HaveCount(3);
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task GetPendingMessagesAsync_ShouldOrderByCreatedAt()
    {
        var store = CreateStore();
        var ids = new List<long>();
        for (int i = 0; i < 5; i++)
        {
            var id = NextMessageId();
            ids.Add(id);
            await store.AddAsync(new OutboxMessage
            {
                MessageId = id,
                MessageType = "Type",
                Payload = [(byte)i]
            });
            await Task.Delay(10); // Ensure different CreatedAt
        }

        var pending = await store.GetPendingMessagesAsync(10);

        // Verify messages are ordered by CreatedAt
        for (int i = 1; i < pending.Count; i++)
        {
            pending[i].CreatedAt.Should().BeOnOrAfter(pending[i - 1].CreatedAt);
        }
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task GetPendingMessagesAsync_ShouldNotReturnPublished()
    {
        var store = CreateStore();
        var id1 = NextMessageId();
        var id2 = NextMessageId();
        await store.AddAsync(new OutboxMessage { MessageId = id1, MessageType = "Type", Payload = [1] });
        await store.AddAsync(new OutboxMessage { MessageId = id2, MessageType = "Type", Payload = [2] });
        await store.MarkAsPublishedAsync(id1);

        var pending = await store.GetPendingMessagesAsync(10);

        pending.Should().ContainSingle(m => m.MessageId == id2);
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task GetPendingMessagesAsync_ShouldNotReturnExceededRetries()
    {
        var store = CreateStore();
        var id = NextMessageId();
        await store.AddAsync(new OutboxMessage { MessageId = id, MessageType = "Type", Payload = [1], MaxRetries = 2 });
        await store.MarkAsFailedAsync(id, "Error 1");
        await store.MarkAsFailedAsync(id, "Error 2");

        var pending = await store.GetPendingMessagesAsync(10);

        pending.Should().NotContain(m => m.MessageId == id);
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task MarkAsPublishedAsync_ShouldSetStatusToPublished()
    {
        var store = CreateStore();
        var id = NextMessageId();
        await store.AddAsync(new OutboxMessage { MessageId = id, MessageType = "Type", Payload = [1] });

        await store.MarkAsPublishedAsync(id);

        var pending = await store.GetPendingMessagesAsync(10);
        pending.Should().NotContain(m => m.MessageId == id);
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task MarkAsFailedAsync_ShouldIncrementRetryCount()
    {
        var store = CreateStore();
        var id = NextMessageId();
        await store.AddAsync(new OutboxMessage { MessageId = id, MessageType = "Type", Payload = [1], MaxRetries = 10 });

        await store.MarkAsFailedAsync(id, "Test error");

        var pending = await store.GetPendingMessagesAsync(10);
        pending.Should().ContainSingle(m => m.MessageId == id && m.RetryCount == 1);
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task MarkAsFailedAsync_MultipleTimes_ShouldAccumulateRetryCount()
    {
        var store = CreateStore();
        var id = NextMessageId();
        await store.AddAsync(new OutboxMessage { MessageId = id, MessageType = "Type", Payload = [1], MaxRetries = 10 });

        await store.MarkAsFailedAsync(id, "Error 1");
        await store.MarkAsFailedAsync(id, "Error 2");
        await store.MarkAsFailedAsync(id, "Error 3");

        var pending = await store.GetPendingMessagesAsync(10);
        pending.Should().ContainSingle(m => m.MessageId == id && m.RetryCount == 3);
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task MarkAsFailedAsync_ExceedMaxRetries_ShouldSetStatusToFailed()
    {
        var store = CreateStore();
        var id = NextMessageId();
        await store.AddAsync(new OutboxMessage { MessageId = id, MessageType = "Type", Payload = [1], MaxRetries = 2 });

        await store.MarkAsFailedAsync(id, "Error 1");
        await store.MarkAsFailedAsync(id, "Error 2");

        var pending = await store.GetPendingMessagesAsync(10);
        pending.Should().NotContain(m => m.MessageId == id);
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task MarkAsFailedAsync_ShouldStoreLastError()
    {
        var store = CreateStore();
        var id = NextMessageId();
        await store.AddAsync(new OutboxMessage { MessageId = id, MessageType = "Type", Payload = [1], MaxRetries = 10 });

        await store.MarkAsFailedAsync(id, "Specific error message");

        var pending = await store.GetPendingMessagesAsync(10);
        pending.Should().ContainSingle(m => m.MessageId == id && m.LastError == "Specific error message");
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task DeletePublishedMessagesAsync_ShouldNotThrow()
    {
        var store = CreateStore();

        var act = async () => await store.DeletePublishedMessagesAsync(TimeSpan.FromDays(1));

        await act.Should().NotThrowAsync();
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task MultipleMessages_ShouldBeIndependent()
    {
        var store = CreateStore();
        var id1 = NextMessageId();
        var id2 = NextMessageId();
        var id3 = NextMessageId();

        await store.AddAsync(new OutboxMessage { MessageId = id1, MessageType = "T1", Payload = [1] });
        await store.AddAsync(new OutboxMessage { MessageId = id2, MessageType = "T2", Payload = [2] });
        await store.AddAsync(new OutboxMessage { MessageId = id3, MessageType = "T3", Payload = [3] });
        await store.MarkAsPublishedAsync(id1);
        await store.MarkAsFailedAsync(id2, "Error");

        var pending = await store.GetPendingMessagesAsync(10);
        pending.Should().HaveCount(2);
        pending.Should().Contain(m => m.MessageId == id2);
        pending.Should().Contain(m => m.MessageId == id3);
        await CleanupAsync();
    }

    [SkippableFact]
    public async Task ConcurrentAdds_ShouldBeThreadSafe()
    {
        var store = CreateStore();
        var tasks = Enumerable.Range(0, 50).Select(i => store.AddAsync(new OutboxMessage
        {
            MessageId = NextMessageId(),
            MessageType = "Type",
            Payload = [(byte)i]
        }).AsTask());

        await Task.WhenAll(tasks);

        var pending = await store.GetPendingMessagesAsync(100);
        pending.Should().HaveCount(50);
        await CleanupAsync();
    }
}


#region InMemory Tests

/// <summary>
/// InMemory Inbox Store Tests
/// </summary>
public class MemoryInboxStoreComprehensiveTests : InboxStoreTestsBase
{
    private MemoryInboxStore? _store;

    protected override IInboxStore CreateStore()
    {
        var provider = new DefaultResiliencePipelineProvider();
        _store = new MemoryInboxStore(provider);
        return _store;
    }

    protected override Task CleanupAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetMessageCountByStatus_ShouldReturnCorrectCount()
    {
        var store = (MemoryInboxStore)CreateStore();
        var id1 = NextMessageId();
        var id2 = NextMessageId();
        var id3 = NextMessageId();

        await store.MarkAsProcessedAsync(new InboxMessage { MessageId = id1, MessageType = "T", Payload = [] });
        await store.MarkAsProcessedAsync(new InboxMessage { MessageId = id2, MessageType = "T", Payload = [] });
        await store.TryLockMessageAsync(id3, TimeSpan.FromMinutes(5));

        store.GetMessageCountByStatus(InboxStatus.Processed).Should().Be(2);
        store.GetMessageCountByStatus(InboxStatus.Processing).Should().Be(1);
    }
}

/// <summary>
/// InMemory Outbox Store Tests
/// </summary>
public class MemoryOutboxStoreComprehensiveTests : OutboxStoreTestsBase
{
    private MemoryOutboxStore? _store;

    protected override IOutboxStore CreateStore()
    {
        var provider = new DefaultResiliencePipelineProvider();
        _store = new MemoryOutboxStore(provider);
        return _store;
    }

    protected override Task CleanupAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetMessageCountByStatus_ShouldReturnCorrectCount()
    {
        var store = (MemoryOutboxStore)CreateStore();
        var id1 = NextMessageId();
        var id2 = NextMessageId();
        var id3 = NextMessageId();

        await store.AddAsync(new OutboxMessage { MessageId = id1, MessageType = "T", Payload = [] });
        await store.AddAsync(new OutboxMessage { MessageId = id2, MessageType = "T", Payload = [] });
        await store.AddAsync(new OutboxMessage { MessageId = id3, MessageType = "T", Payload = [] });
        await store.MarkAsPublishedAsync(id1);

        store.GetMessageCountByStatus(OutboxStatus.Pending).Should().Be(2);
        store.GetMessageCountByStatus(OutboxStatus.Published).Should().Be(1);
    }

    [Fact]
    public async Task AddAsync_ZeroMessageId_ShouldThrow()
    {
        var store = CreateStore();

        var act = async () => await store.AddAsync(new OutboxMessage { MessageId = 0, MessageType = "T", Payload = [] });

        await act.Should().ThrowAsync<ArgumentException>();
    }
}

#endregion

#region Redis Tests

/// <summary>
/// Redis Inbox Store Tests - requires Redis running on localhost:6379
/// </summary>
[Collection("Redis")]
public class RedisInboxStoreComprehensiveTests : InboxStoreTestsBase
{
    private IConnectionMultiplexer? _redis;
    private readonly string _testPrefix = $"test:inbox:{Guid.NewGuid():N}:";
    private static readonly bool _redisAvailable = CheckRedisAvailable();

    private static bool CheckRedisAvailable()
    {
        try
        {
            using var redis = ConnectionMultiplexer.Connect("localhost:6379,allowAdmin=true,connectTimeout=1000,abortConnect=false");
            return redis.IsConnected;
        }
        catch
        {
            return false;
        }
    }

    protected override IInboxStore CreateStore()
    {
        Skip.IfNot(_redisAvailable, "Redis not available");
        _redis = ConnectionMultiplexer.Connect("localhost:6379,allowAdmin=true,connectTimeout=1000");
        var serializer = new MemoryPackMessageSerializer();
        var provider = new DefaultResiliencePipelineProvider();
        var options = Options.Create(new RedisInboxStoreOptions { KeyPrefix = _testPrefix });
        return new RedisInboxStore(_redis, serializer, provider, options);
    }

    protected override async Task CleanupAsync()
    {
        if (_redis != null)
        {
            var server = _redis.GetServers().FirstOrDefault();
            if (server != null)
            {
                var db = _redis.GetDatabase();
                await foreach (var key in server.KeysAsync(pattern: $"{_testPrefix}*"))
                    await db.KeyDeleteAsync(key);
            }
            await _redis.CloseAsync();
        }
    }
}

/// <summary>
/// Redis Outbox Store Tests - requires Redis running on localhost:6379
/// </summary>
[Collection("Redis")]
public class RedisOutboxStoreComprehensiveTests : OutboxStoreTestsBase
{
    private IConnectionMultiplexer? _redis;
    private readonly string _testPrefix = $"test:outbox:{Guid.NewGuid():N}:";
    private readonly string _testPendingKey;
    private static readonly bool _redisAvailable = CheckRedisAvailable();

    private static bool CheckRedisAvailable()
    {
        try
        {
            using var redis = ConnectionMultiplexer.Connect("localhost:6379,allowAdmin=true,connectTimeout=1000,abortConnect=false");
            return redis.IsConnected;
        }
        catch
        {
            return false;
        }
    }

    public RedisOutboxStoreComprehensiveTests()
    {
        _testPendingKey = $"{_testPrefix}pending";
    }

    protected override IOutboxStore CreateStore()
    {
        Skip.IfNot(_redisAvailable, "Redis not available");
        _redis = ConnectionMultiplexer.Connect("localhost:6379,allowAdmin=true,connectTimeout=1000");
        var serializer = new MemoryPackMessageSerializer();
        var provider = new DefaultResiliencePipelineProvider();
        var options = Options.Create(new RedisOutboxStoreOptions { KeyPrefix = _testPrefix, PendingSetKey = _testPendingKey });
        return new RedisOutboxStore(_redis, serializer, provider, options);
    }

    protected override async Task CleanupAsync()
    {
        if (_redis != null)
        {
            var server = _redis.GetServers().FirstOrDefault();
            if (server != null)
            {
                var db = _redis.GetDatabase();
                await foreach (var key in server.KeysAsync(pattern: $"{_testPrefix}*"))
                    await db.KeyDeleteAsync(key);
            }
            await _redis.CloseAsync();
        }
    }
}

#endregion

#region NATS Tests

/// <summary>
/// NATS Inbox Store Tests - requires NATS running on localhost:4222
/// </summary>
[Collection("Nats")]
public class NatsInboxStoreComprehensiveTests : InboxStoreTestsBase
{
    private INatsConnection? _connection;
    private readonly string _streamName = $"TEST_INBOX_{Guid.NewGuid():N}";
    private static readonly bool _natsAvailable = CheckNatsAvailable();

    private static bool CheckNatsAvailable()
    {
        try
        {
            var connection = new NatsConnection(new NatsOpts { Url = "nats://localhost:4222", ConnectTimeout = TimeSpan.FromSeconds(1) });
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return true;
        }
        catch
        {
            return false;
        }
    }

    protected override IInboxStore CreateStore()
    {
        Skip.IfNot(_natsAvailable, "NATS not available");
        _connection = new NatsConnection(new NatsOpts { Url = "nats://localhost:4222", ConnectTimeout = TimeSpan.FromSeconds(1) });
        var serializer = new MemoryPackMessageSerializer();
        var provider = new DefaultResiliencePipelineProvider();
        return new NatsJSInboxStore(_connection, serializer, provider, _streamName);
    }

    protected override async Task CleanupAsync()
    {
        if (_connection != null)
        {
            try
            {
                var js = new NatsJSContext(_connection);
                await js.DeleteStreamAsync(_streamName);
            }
            catch { }
            await _connection.DisposeAsync();
        }
    }
}

/// <summary>
/// NATS Outbox Store Tests - requires NATS running on localhost:4222
/// </summary>
[Collection("Nats")]
public class NatsOutboxStoreComprehensiveTests : OutboxStoreTestsBase
{
    private INatsConnection? _connection;
    private readonly string _streamName = $"TEST_OUTBOX_{Guid.NewGuid():N}";
    private static readonly bool _natsAvailable = CheckNatsAvailable();

    private static bool CheckNatsAvailable()
    {
        try
        {
            var connection = new NatsConnection(new NatsOpts { Url = "nats://localhost:4222", ConnectTimeout = TimeSpan.FromSeconds(1) });
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return true;
        }
        catch
        {
            return false;
        }
    }

    protected override IOutboxStore CreateStore()
    {
        Skip.IfNot(_natsAvailable, "NATS not available");
        _connection = new NatsConnection(new NatsOpts { Url = "nats://localhost:4222", ConnectTimeout = TimeSpan.FromSeconds(1) });
        var serializer = new MemoryPackMessageSerializer();
        var provider = new DefaultResiliencePipelineProvider();
        return new NatsJSOutboxStore(_connection, serializer, provider, _streamName);
    }

    protected override async Task CleanupAsync()
    {
        if (_connection != null)
        {
            try
            {
                var js = new NatsJSContext(_connection);
                await js.DeleteStreamAsync(_streamName);
            }
            catch { }
            await _connection.DisposeAsync();
        }
    }
}

#endregion
