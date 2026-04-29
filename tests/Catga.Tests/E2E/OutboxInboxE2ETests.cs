using Catga.DependencyInjection;
using Catga.Inbox;
using Catga.Outbox;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Tests.E2E;

public class OutboxInboxE2ETests
{
    [Fact]
    public async Task Outbox_AddMessage_StoredSuccessfully()
    {
        var sp = CreateProvider();
        var outboxStore = sp.GetRequiredService<IOutboxStore>();
        var message = CreateOutboxMessage(1001);

        await outboxStore.AddAsync(message);

        var pending = await outboxStore.GetPendingMessagesAsync(10);

        pending.Should().ContainSingle(m => m.MessageId == message.MessageId);
    }

    [Fact]
    public async Task Outbox_MarkAsPublished_RemovesFromPending()
    {
        var sp = CreateProvider();
        var outboxStore = sp.GetRequiredService<IOutboxStore>();
        var message = CreateOutboxMessage(1002);

        await outboxStore.AddAsync(message);
        await outboxStore.MarkAsPublishedAsync(message.MessageId);

        var pending = await outboxStore.GetPendingMessagesAsync(10);

        pending.Should().NotContain(m => m.MessageId == message.MessageId);
    }

    [Fact]
    public async Task Outbox_BatchMessages_ReturnsInCreatedOrder()
    {
        var sp = CreateProvider();
        var outboxStore = sp.GetRequiredService<IOutboxStore>();

        for (var i = 1; i <= 5; i++)
        {
            await outboxStore.AddAsync(new OutboxMessage
            {
                MessageId = 2000 + i,
                MessageType = "SequenceMessage",
                Payload = [(byte)i],
                CreatedAt = DateTime.UtcNow.AddMilliseconds(i)
            });
        }

        var pending = await outboxStore.GetPendingMessagesAsync(10);

        pending.Select(m => m.MessageId).Should().Equal(2001, 2002, 2003, 2004, 2005);
    }

    [Fact]
    public async Task Outbox_LimitedBatch_ReturnsOnlyRequestedCount()
    {
        var sp = CreateProvider();
        var outboxStore = sp.GetRequiredService<IOutboxStore>();

        for (var i = 1; i <= 10; i++)
        {
            await outboxStore.AddAsync(CreateOutboxMessage(3000 + i));
        }

        var batch = await outboxStore.GetPendingMessagesAsync(5);

        batch.Should().HaveCount(5);
    }

    [Fact]
    public async Task Outbox_MarkAsFailed_RequeuesUntilMaxRetries()
    {
        var sp = CreateProvider();
        var outboxStore = sp.GetRequiredService<IOutboxStore>();
        var message = new OutboxMessage
        {
            MessageId = 4001,
            MessageType = "RetryMessage",
            Payload = [1, 2, 3],
            MaxRetries = 3
        };

        await outboxStore.AddAsync(message);
        await outboxStore.MarkAsFailedAsync(message.MessageId, "fail-1");
        await outboxStore.MarkAsFailedAsync(message.MessageId, "fail-2");

        var pending = await outboxStore.GetPendingMessagesAsync(10);
        var retried = pending.Single(m => m.MessageId == message.MessageId);

        retried.RetryCount.Should().Be(2);
        retried.Status.Should().Be(OutboxStatus.Pending);
        retried.LastError.Should().Be("fail-2");
    }

    [Fact]
    public async Task Outbox_MarkAsFailed_AfterMaxRetries_MovesToFailed()
    {
        var sp = CreateProvider();
        var outboxStore = sp.GetRequiredService<IOutboxStore>();
        var message = new OutboxMessage
        {
            MessageId = 4002,
            MessageType = "RetryMessage",
            Payload = [1, 2, 3],
            MaxRetries = 2
        };

        await outboxStore.AddAsync(message);
        await outboxStore.MarkAsFailedAsync(message.MessageId, "fail-1");
        await outboxStore.MarkAsFailedAsync(message.MessageId, "fail-2");

        var pending = await outboxStore.GetPendingMessagesAsync(10);

        pending.Should().NotContain(m => m.MessageId == message.MessageId);
    }

    [Fact]
    public async Task Outbox_ScheduledMessages_DefaultImplementation_ReturnsDueMessage()
    {
        var sp = CreateProvider();
        var outboxStore = sp.GetRequiredService<IOutboxStore>();
        var message = new OutboxMessage
        {
            MessageId = 5001,
            MessageType = "ScheduledMessage",
            Payload = [9],
            ScheduledAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        await outboxStore.AddAsync(message);

        var due = await outboxStore.GetDueScheduledMessagesAsync(10);

        due.Should().ContainSingle(m => m.MessageId == message.MessageId);
    }

    [Fact]
    public async Task Inbox_TryLock_PreventsConcurrentDuplicates()
    {
        var sp = CreateProvider();
        var inboxStore = sp.GetRequiredService<IInboxStore>();
        const long messageId = 6001;

        var first = await inboxStore.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
        var second = await inboxStore.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    [Fact]
    public async Task Inbox_ReleaseLock_AllowsRelock()
    {
        var sp = CreateProvider();
        var inboxStore = sp.GetRequiredService<IInboxStore>();
        const long messageId = 6002;

        await inboxStore.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
        await inboxStore.ReleaseLockAsync(messageId);

        var relocked = await inboxStore.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        relocked.Should().BeTrue();
    }

    [Fact]
    public async Task Inbox_MarkAsProcessed_PersistsStatusAndResult()
    {
        var sp = CreateProvider();
        var inboxStore = sp.GetRequiredService<IInboxStore>();
        var resultPayload = new byte[] { 7, 8, 9 };
        var message = new InboxMessage
        {
            MessageId = 6003,
            MessageType = "ProcessedMessage",
            Payload = [1, 2, 3],
            ProcessingResult = resultPayload
        };

        await inboxStore.TryLockMessageAsync(message.MessageId, TimeSpan.FromMinutes(5));
        await inboxStore.MarkAsProcessedAsync(message);

        var processed = await inboxStore.HasBeenProcessedAsync(message.MessageId);
        var storedResult = await inboxStore.GetProcessedResultAsync(message.MessageId);
        var relocked = await inboxStore.TryLockMessageAsync(message.MessageId, TimeSpan.FromMinutes(5));

        processed.Should().BeTrue();
        storedResult.Should().BeEquivalentTo(resultPayload);
        relocked.Should().BeFalse();
    }

    [Fact]
    public async Task Inbox_ConcurrentDuplicates_OnlyOneLockSucceeds()
    {
        var sp = CreateProvider();
        var inboxStore = sp.GetRequiredService<IInboxStore>();
        const long messageId = 6004;
        var successCount = 0;

        var tasks = Enumerable.Range(1, 10).Select(async _ =>
        {
            var locked = await inboxStore.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
            if (locked)
            {
                Interlocked.Increment(ref successCount);
            }

            return locked;
        });

        await Task.WhenAll(tasks);

        successCount.Should().Be(1);
    }

    [Fact]
    public async Task Inbox_ExpiredLock_CanBeClaimedAgain()
    {
        var sp = CreateProvider();
        var inboxStore = sp.GetRequiredService<IInboxStore>();
        const long messageId = 6005;

        await inboxStore.TryLockMessageAsync(messageId, TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);

        var relocked = await inboxStore.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        relocked.Should().BeTrue();
    }

    [Fact]
    public async Task OutboxProcessor_SimulatedLoop_PublishesAndClearsPending()
    {
        var sp = CreateProvider();
        var outboxStore = sp.GetRequiredService<IOutboxStore>();

        for (var i = 0; i < 5; i++)
        {
            await outboxStore.AddAsync(CreateOutboxMessage(7000 + i));
        }

        var processedIds = new List<long>();
        var pending = await outboxStore.GetPendingMessagesAsync(10);
        foreach (var message in pending)
        {
            processedIds.Add(message.MessageId);
            await outboxStore.MarkAsPublishedAsync(message.MessageId);
        }

        var remaining = await outboxStore.GetPendingMessagesAsync(10);

        processedIds.Should().HaveCount(5);
        remaining.Should().NotContain(m => processedIds.Contains(m.MessageId));
    }

    [Fact]
    public async Task Outbox_LargePayload_HandledCorrectly()
    {
        var sp = CreateProvider();
        var outboxStore = sp.GetRequiredService<IOutboxStore>();
        var largePayload = new byte[100_000];
        Random.Shared.NextBytes(largePayload);
        var message = new OutboxMessage
        {
            MessageId = 8001,
            MessageType = "LargeMessage",
            Payload = largePayload
        };

        await outboxStore.AddAsync(message);

        var pending = await outboxStore.GetPendingMessagesAsync(10);
        var stored = pending.Single(m => m.MessageId == message.MessageId);

        stored.Payload.Should().HaveCount(100_000);
        stored.Payload.Should().BeEquivalentTo(largePayload);
    }

    private static ServiceProvider CreateProvider()
        => new ServiceCollection()
            .AddCatga(opt => opt.ForDevelopment())
            .UseMemoryPack()
            .UseInMemory()
            .Services
            .BuildServiceProvider();

    private static OutboxMessage CreateOutboxMessage(long messageId)
        => new()
        {
            MessageId = messageId,
            MessageType = "TestMessage",
            Payload = [1, 2, 3],
            CreatedAt = DateTime.UtcNow
        };
}
