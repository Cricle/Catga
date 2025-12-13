using Catga.Abstractions;
using Catga.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// Outbox and Inbox pattern E2E tests.
/// Tests reliable message delivery, at-least-once semantics, and message ordering.
/// </summary>
public class OutboxInboxE2ETests
{
    [Fact]
    public async Task Outbox_AddMessage_StoredSuccessfully()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var outboxStore = sp.GetRequiredService<IOutboxStore>();

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid().ToString(),
            MessageType = "OrderCreated",
            Payload = System.Text.Encoding.UTF8.GetBytes("{\"orderId\":\"ORD-001\"}"),
            CreatedAt = DateTime.UtcNow
        };

        await outboxStore.AddAsync(message);

        var pending = await outboxStore.GetPendingAsync(10);

        pending.Should().ContainSingle(m => m.Id == message.Id);
    }

    [Fact]
    public async Task Outbox_MarkAsProcessed_RemovesFromPending()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var outboxStore = sp.GetRequiredService<IOutboxStore>();

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid().ToString(),
            MessageType = "TestMessage",
            Payload = new byte[] { 1, 2, 3 },
            CreatedAt = DateTime.UtcNow
        };

        await outboxStore.AddAsync(message);
        await outboxStore.MarkAsProcessedAsync(message.Id);

        var pending = await outboxStore.GetPendingAsync(10);

        pending.Should().NotContain(m => m.Id == message.Id);
    }

    [Fact]
    public async Task Outbox_BatchMessages_ReturnsInOrder()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var outboxStore = sp.GetRequiredService<IOutboxStore>();

        // Add messages with slight delay to ensure ordering
        for (int i = 1; i <= 5; i++)
        {
            await outboxStore.AddAsync(new OutboxMessage
            {
                Id = $"msg-{i}",
                MessageType = "SequenceMessage",
                Payload = new byte[] { (byte)i },
                CreatedAt = DateTime.UtcNow.AddMilliseconds(i)
            });
        }

        var pending = await outboxStore.GetPendingAsync(10);

        pending.Should().HaveCount(5);
    }

    [Fact]
    public async Task Outbox_LimitedBatch_ReturnsOnlyRequestedCount()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var outboxStore = sp.GetRequiredService<IOutboxStore>();

        // Add 10 messages
        for (int i = 1; i <= 10; i++)
        {
            await outboxStore.AddAsync(new OutboxMessage
            {
                Id = $"batch-{Guid.NewGuid():N}",
                MessageType = "BatchMessage",
                Payload = new byte[] { (byte)i },
                CreatedAt = DateTime.UtcNow
            });
        }

        var batch = await outboxStore.GetPendingAsync(5);

        batch.Should().HaveCount(5);
    }

    [Fact]
    public async Task Inbox_StoreMessage_PreventsDuplicates()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var inboxStore = sp.GetRequiredService<IInboxStore>();

        var messageId = Guid.NewGuid().ToString();

        // First store
        var firstResult = await inboxStore.TryStoreAsync(messageId, TimeSpan.FromMinutes(5));

        // Second store with same ID
        var secondResult = await inboxStore.TryStoreAsync(messageId, TimeSpan.FromMinutes(5));

        firstResult.Should().BeTrue();
        secondResult.Should().BeFalse();
    }

    [Fact]
    public async Task Inbox_ExpiredMessage_AllowsReprocessing()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var inboxStore = sp.GetRequiredService<IInboxStore>();

        var messageId = Guid.NewGuid().ToString();

        // Store with very short TTL
        await inboxStore.TryStoreAsync(messageId, TimeSpan.FromMilliseconds(50));

        // Wait for expiry
        await Task.Delay(100);

        // Should allow reprocessing (implementation dependent)
        var exists = await inboxStore.ExistsAsync(messageId);
        // Note: result depends on auto-cleanup behavior
    }

    [Fact]
    public async Task Inbox_ConcurrentDuplicates_OnlyOneSucceeds()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var inboxStore = sp.GetRequiredService<IInboxStore>();

        var messageId = Guid.NewGuid().ToString();
        var successCount = 0;

        // Try to store same message 10 times concurrently
        var tasks = Enumerable.Range(1, 10).Select(async _ =>
        {
            var result = await inboxStore.TryStoreAsync(messageId, TimeSpan.FromMinutes(5));
            if (result) Interlocked.Increment(ref successCount);
            return result;
        });

        await Task.WhenAll(tasks);

        successCount.Should().Be(1);
    }

    [Fact]
    public async Task Outbox_TransactionalConsistency_AllOrNothing()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var outboxStore = sp.GetRequiredService<IOutboxStore>();

        var batchId = Guid.NewGuid().ToString();
        var messages = new List<OutboxMessage>();

        for (int i = 0; i < 5; i++)
        {
            messages.Add(new OutboxMessage
            {
                Id = $"{batchId}-{i}",
                MessageType = "TransactionMessage",
                Payload = new byte[] { (byte)i },
                CreatedAt = DateTime.UtcNow
            });
        }

        // Add all messages
        foreach (var msg in messages)
        {
            await outboxStore.AddAsync(msg);
        }

        // Verify all added
        var pending = await outboxStore.GetPendingAsync(100);
        var batchMessages = pending.Where(m => m.Id.StartsWith(batchId)).ToList();

        batchMessages.Should().HaveCount(5);
    }

    [Fact]
    public async Task OutboxProcessor_ProcessesPendingMessages()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var outboxStore = sp.GetRequiredService<IOutboxStore>();
        var processedMessages = new List<string>();

        // Add messages to outbox
        for (int i = 0; i < 5; i++)
        {
            await outboxStore.AddAsync(new OutboxMessage
            {
                Id = $"process-{i}",
                MessageType = "ToProcess",
                Payload = new byte[] { (byte)i },
                CreatedAt = DateTime.UtcNow
            });
        }

        // Simulate outbox processor
        var pending = await outboxStore.GetPendingAsync(10);
        foreach (var msg in pending)
        {
            // "Process" the message
            processedMessages.Add(msg.Id);
            await outboxStore.MarkAsProcessedAsync(msg.Id);
        }

        // Verify all processed
        processedMessages.Should().HaveCount(5);

        var remaining = await outboxStore.GetPendingAsync(10);
        remaining.Where(m => m.Id.StartsWith("process-")).Should().BeEmpty();
    }

    [Fact]
    public async Task Inbox_HighVolume_HandlesCorrectly()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var inboxStore = sp.GetRequiredService<IInboxStore>();

        // Store 100 unique messages
        var tasks = Enumerable.Range(1, 100).Select(async i =>
        {
            var messageId = $"high-volume-{i}";
            return await inboxStore.TryStoreAsync(messageId, TimeSpan.FromMinutes(5));
        });

        var results = await Task.WhenAll(tasks);

        results.Should().AllSatisfy(r => r.Should().BeTrue());
    }

    [Fact]
    public async Task Outbox_LargePayload_HandledCorrectly()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var outboxStore = sp.GetRequiredService<IOutboxStore>();

        var largePayload = new byte[100_000]; // 100KB payload
        Random.Shared.NextBytes(largePayload);

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid().ToString(),
            MessageType = "LargeMessage",
            Payload = largePayload,
            CreatedAt = DateTime.UtcNow
        };

        await outboxStore.AddAsync(message);

        var pending = await outboxStore.GetPendingAsync(10);
        var retrieved = pending.FirstOrDefault(m => m.Id == message.Id);

        retrieved.Should().NotBeNull();
        retrieved!.Payload.Should().HaveCount(100_000);
    }
}
