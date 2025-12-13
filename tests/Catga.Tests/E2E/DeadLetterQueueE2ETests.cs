using Catga.Abstractions;
using Catga.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// Dead Letter Queue E2E tests.
/// Tests failed message handling, retry policies, and poison message management.
/// </summary>
public class DeadLetterQueueE2ETests
{
    [Fact]
    public async Task DLQ_FailedMessage_StoredInDeadLetter()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var dlqStore = sp.GetRequiredService<IDeadLetterStore>();

        var deadLetter = new DeadLetterMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            OriginalQueue = "orders-queue",
            Payload = System.Text.Encoding.UTF8.GetBytes("{\"orderId\":\"ORD-001\"}"),
            FailedAt = DateTime.UtcNow,
            Reason = "Processing timeout"
        };

        await dlqStore.StoreAsync(deadLetter);

        var messages = await dlqStore.GetMessagesAsync("orders-queue", 10);

        messages.Should().ContainSingle(m => m.MessageId == deadLetter.MessageId);
    }

    [Fact]
    public async Task DLQ_RetryMessage_RemovesFromDLQ()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var dlqStore = sp.GetRequiredService<IDeadLetterStore>();

        var messageId = Guid.NewGuid().ToString();
        var deadLetter = new DeadLetterMessage
        {
            MessageId = messageId,
            OriginalQueue = "test-queue",
            Payload = new byte[] { 1, 2, 3 },
            FailedAt = DateTime.UtcNow,
            Reason = "Test failure"
        };

        await dlqStore.StoreAsync(deadLetter);
        await dlqStore.RemoveAsync(messageId);

        var messages = await dlqStore.GetMessagesAsync("test-queue", 10);

        messages.Should().NotContain(m => m.MessageId == messageId);
    }

    [Fact]
    public async Task DLQ_MultipleQueues_IsolatedMessages()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var dlqStore = sp.GetRequiredService<IDeadLetterStore>();

        // Add to different queues
        await dlqStore.StoreAsync(new DeadLetterMessage
        {
            MessageId = "msg-queue-a",
            OriginalQueue = "queue-a",
            Payload = new byte[] { 1 },
            FailedAt = DateTime.UtcNow,
            Reason = "Error A"
        });

        await dlqStore.StoreAsync(new DeadLetterMessage
        {
            MessageId = "msg-queue-b",
            OriginalQueue = "queue-b",
            Payload = new byte[] { 2 },
            FailedAt = DateTime.UtcNow,
            Reason = "Error B"
        });

        var queueAMessages = await dlqStore.GetMessagesAsync("queue-a", 10);
        var queueBMessages = await dlqStore.GetMessagesAsync("queue-b", 10);

        queueAMessages.Should().HaveCount(1);
        queueBMessages.Should().HaveCount(1);
        queueAMessages.First().MessageId.Should().Be("msg-queue-a");
        queueBMessages.First().MessageId.Should().Be("msg-queue-b");
    }

    [Fact]
    public async Task DLQ_RetryCount_TracksAttempts()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var dlqStore = sp.GetRequiredService<IDeadLetterStore>();

        var messageId = Guid.NewGuid().ToString();

        // First failure
        await dlqStore.StoreAsync(new DeadLetterMessage
        {
            MessageId = messageId,
            OriginalQueue = "retry-queue",
            Payload = new byte[] { 1 },
            FailedAt = DateTime.UtcNow,
            Reason = "Attempt 1 failed",
            RetryCount = 1
        });

        // Update with retry count
        await dlqStore.RemoveAsync(messageId);
        await dlqStore.StoreAsync(new DeadLetterMessage
        {
            MessageId = messageId,
            OriginalQueue = "retry-queue",
            Payload = new byte[] { 1 },
            FailedAt = DateTime.UtcNow,
            Reason = "Attempt 2 failed",
            RetryCount = 2
        });

        var messages = await dlqStore.GetMessagesAsync("retry-queue", 10);

        messages.Should().ContainSingle();
        messages.First().RetryCount.Should().Be(2);
    }

    [Fact]
    public async Task DLQ_PoisonMessage_MarkedAsPermanentFailure()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var dlqStore = sp.GetRequiredService<IDeadLetterStore>();

        var poisonMessage = new DeadLetterMessage
        {
            MessageId = "poison-001",
            OriginalQueue = "main-queue",
            Payload = new byte[] { 0xFF, 0xFE }, // Malformed data
            FailedAt = DateTime.UtcNow,
            Reason = "Deserialization failed - permanent failure",
            RetryCount = 3,
            IsPermanentFailure = true
        };

        await dlqStore.StoreAsync(poisonMessage);

        var messages = await dlqStore.GetMessagesAsync("main-queue", 10);

        messages.Should().ContainSingle(m => m.IsPermanentFailure);
    }

    [Fact]
    public async Task DLQ_Pagination_ReturnsLimitedResults()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var dlqStore = sp.GetRequiredService<IDeadLetterStore>();

        // Add 20 messages
        for (int i = 0; i < 20; i++)
        {
            await dlqStore.StoreAsync(new DeadLetterMessage
            {
                MessageId = $"page-msg-{i}",
                OriginalQueue = "paginated-queue",
                Payload = new byte[] { (byte)i },
                FailedAt = DateTime.UtcNow,
                Reason = $"Error {i}"
            });
        }

        var firstPage = await dlqStore.GetMessagesAsync("paginated-queue", 5);
        var secondPage = await dlqStore.GetMessagesAsync("paginated-queue", 10);

        firstPage.Should().HaveCount(5);
        secondPage.Should().HaveCount(10);
    }

    [Fact]
    public async Task DLQ_MessageDetails_PreservesAllInfo()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var dlqStore = sp.GetRequiredService<IDeadLetterStore>();

        var originalPayload = System.Text.Encoding.UTF8.GetBytes("{\"data\":\"test\"}");
        var failedAt = DateTime.UtcNow;

        var deadLetter = new DeadLetterMessage
        {
            MessageId = "detailed-001",
            OriginalQueue = "detailed-queue",
            Payload = originalPayload,
            FailedAt = failedAt,
            Reason = "Connection timeout after 30s",
            RetryCount = 2,
            Headers = new Dictionary<string, string>
            {
                ["CorrelationId"] = "corr-123",
                ["Source"] = "OrderService"
            }
        };

        await dlqStore.StoreAsync(deadLetter);

        var messages = await dlqStore.GetMessagesAsync("detailed-queue", 10);
        var retrieved = messages.First();

        retrieved.MessageId.Should().Be("detailed-001");
        retrieved.Payload.Should().BeEquivalentTo(originalPayload);
        retrieved.Reason.Should().Contain("timeout");
        retrieved.RetryCount.Should().Be(2);
    }

    [Fact]
    public async Task DLQ_BulkOperations_HandlesMultipleMessages()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var dlqStore = sp.GetRequiredService<IDeadLetterStore>();

        // Bulk store
        var messages = Enumerable.Range(1, 10).Select(i => new DeadLetterMessage
        {
            MessageId = $"bulk-{i}",
            OriginalQueue = "bulk-queue",
            Payload = new byte[] { (byte)i },
            FailedAt = DateTime.UtcNow,
            Reason = $"Bulk error {i}"
        }).ToList();

        foreach (var msg in messages)
        {
            await dlqStore.StoreAsync(msg);
        }

        // Verify all stored
        var stored = await dlqStore.GetMessagesAsync("bulk-queue", 20);
        stored.Should().HaveCount(10);

        // Bulk remove
        foreach (var msg in messages.Take(5))
        {
            await dlqStore.RemoveAsync(msg.MessageId);
        }

        var remaining = await dlqStore.GetMessagesAsync("bulk-queue", 20);
        remaining.Should().HaveCount(5);
    }

    [Fact]
    public async Task DLQ_TimeBasedRetention_OldMessagesAccessible()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var dlqStore = sp.GetRequiredService<IDeadLetterStore>();

        // Store message with old timestamp
        var oldMessage = new DeadLetterMessage
        {
            MessageId = "old-msg-001",
            OriginalQueue = "retention-queue",
            Payload = new byte[] { 1 },
            FailedAt = DateTime.UtcNow.AddDays(-7), // 7 days old
            Reason = "Old failure"
        };

        await dlqStore.StoreAsync(oldMessage);

        var messages = await dlqStore.GetMessagesAsync("retention-queue", 10);

        messages.Should().ContainSingle(m => m.MessageId == "old-msg-001");
    }

    [Fact]
    public async Task DLQ_EmptyQueue_ReturnsEmptyList()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var dlqStore = sp.GetRequiredService<IDeadLetterStore>();

        var messages = await dlqStore.GetMessagesAsync("nonexistent-queue", 10);

        messages.Should().BeEmpty();
    }
}
