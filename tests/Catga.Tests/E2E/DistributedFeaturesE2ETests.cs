using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.Scheduling;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// E2E tests for distributed features including locking, scheduling, and rate limiting.
/// </summary>
public class DistributedFeaturesE2ETests
{
    [Fact]
    public async Task DistributedLock_AcquireAndRelease_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var lockService = sp.GetRequiredService<IDistributedLock>();
        var resourceId = $"resource-{Guid.NewGuid():N}";

        // Act - Acquire lock
        var acquired = await lockService.TryAcquireAsync(resourceId, TimeSpan.FromMinutes(1));

        // Assert
        acquired.Should().BeTrue("lock should be acquired on first attempt");

        // Act - Try to acquire same lock again (should fail)
        var acquiredAgain = await lockService.TryAcquireAsync(resourceId, TimeSpan.FromMinutes(1));
        acquiredAgain.Should().BeFalse("lock is already held");

        // Act - Release lock
        await lockService.ReleaseAsync(resourceId);

        // Act - Acquire after release
        var acquiredAfterRelease = await lockService.TryAcquireAsync(resourceId, TimeSpan.FromMinutes(1));
        acquiredAfterRelease.Should().BeTrue("lock should be available after release");
    }

    [Fact]
    public async Task DistributedLock_ConcurrentAccess_OnlyOneSucceeds()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var lockService = sp.GetRequiredService<IDistributedLock>();
        var resourceId = $"concurrent-{Guid.NewGuid():N}";

        var successCount = 0;
        var tasks = new List<Task>();

        // Act - Try to acquire lock concurrently
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var acquired = await lockService.TryAcquireAsync(resourceId, TimeSpan.FromMinutes(1));
                if (acquired)
                {
                    Interlocked.Increment(ref successCount);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        successCount.Should().Be(1, "only one concurrent request should acquire the lock");
    }

    [Fact]
    public async Task MessageScheduler_ScheduleAndRetrieve_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var scheduler = sp.GetRequiredService<IMessageScheduler>();

        var scheduledTime = DateTime.UtcNow.AddSeconds(1);
        var message = new TestScheduledMessage { Id = "MSG-001", Content = "Hello" };

        // Act - Schedule message
        var messageId = await scheduler.ScheduleAsync(message, scheduledTime);

        // Assert
        messageId.Should().NotBeNullOrEmpty();

        // Act - Get due messages (should be empty initially)
        var dueNow = await scheduler.GetDueMessagesAsync(DateTime.UtcNow);
        dueNow.Should().BeEmpty("message is scheduled for the future");

        // Wait for scheduled time
        await Task.Delay(1100);

        // Act - Get due messages (should contain our message)
        var dueAfterWait = await scheduler.GetDueMessagesAsync(DateTime.UtcNow);
        dueAfterWait.Should().HaveCount(1);
    }

    [Fact]
    public async Task MessageScheduler_CancelScheduled_RemovesMessage()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var scheduler = sp.GetRequiredService<IMessageScheduler>();

        var scheduledTime = DateTime.UtcNow.AddHours(1);
        var message = new TestScheduledMessage { Id = "MSG-CANCEL", Content = "Cancel me" };

        // Act - Schedule and cancel
        var messageId = await scheduler.ScheduleAsync(message, scheduledTime);
        var cancelled = await scheduler.CancelAsync(messageId);

        // Assert
        cancelled.Should().BeTrue();

        // Verify message is gone
        var dueMessages = await scheduler.GetDueMessagesAsync(scheduledTime.AddMinutes(1));
        dueMessages.Should().NotContain(m => ((TestScheduledMessage)m).Id == "MSG-CANCEL");
    }

    [Fact]
    public async Task Idempotency_PreventsDuplicateProcessing()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var idempotencyStore = sp.GetRequiredService<Catga.Idempotency.IIdempotencyStore>();

        var messageId = $"msg-{Guid.NewGuid():N}";

        // Act - First check (should allow processing)
        var firstCheck = await idempotencyStore.IsProcessedAsync(messageId);
        firstCheck.Should().BeFalse();

        // Mark as processed
        await idempotencyStore.MarkAsProcessedAsync(messageId);

        // Act - Second check (should indicate already processed)
        var secondCheck = await idempotencyStore.IsProcessedAsync(messageId);
        secondCheck.Should().BeTrue();
    }

    [Fact]
    public async Task DeadLetterQueue_StoresFailedMessages()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var dlq = sp.GetRequiredService<Catga.DeadLetter.IDeadLetterQueue>();

        var failedMessage = new TestFailedMessage { Id = "FAIL-001", Reason = "Processing error" };
        var error = new Catga.DeadLetter.DeadLetterError("ProcessingFailed", "Test error", 3);

        // Act
        await dlq.EnqueueAsync(failedMessage, error);

        // Assert
        var messages = await dlq.PeekAsync(10);
        messages.Should().HaveCount(1);

        var retrieved = await dlq.DequeueAsync();
        retrieved.Should().NotBeNull();
        retrieved!.Message.Should().BeOfType<TestFailedMessage>();
        ((TestFailedMessage)retrieved.Message).Id.Should().Be("FAIL-001");
        retrieved.Error.ErrorCode.Should().Be("ProcessingFailed");
    }

    [Fact]
    public async Task Inbox_PreventsDuplicateMessages()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var inbox = sp.GetRequiredService<Catga.Inbox.IInboxStore>();

        var messageId = $"inbox-{Guid.NewGuid():N}";
        var message = new TestInboxMessage { Id = messageId, Data = "Test data" };

        // Act - Store message
        var stored = await inbox.TryAddAsync(messageId, message);
        stored.Should().BeTrue();

        // Act - Try to store same message again
        var storedAgain = await inbox.TryAddAsync(messageId, message);
        storedAgain.Should().BeFalse("duplicate message should be rejected");
    }

    [Fact]
    public async Task Outbox_StoresAndRetrievesPendingMessages()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var outbox = sp.GetRequiredService<Catga.Outbox.IOutboxStore>();

        var message1 = new Catga.Outbox.OutboxMessage(
            Guid.NewGuid().ToString(),
            "TestMessage",
            new byte[] { 1, 2, 3 },
            DateTime.UtcNow);

        var message2 = new Catga.Outbox.OutboxMessage(
            Guid.NewGuid().ToString(),
            "TestMessage",
            new byte[] { 4, 5, 6 },
            DateTime.UtcNow);

        // Act
        await outbox.AddAsync(message1);
        await outbox.AddAsync(message2);

        // Assert
        var pending = await outbox.GetPendingAsync(10);
        pending.Should().HaveCount(2);

        // Mark as published
        await outbox.MarkAsPublishedAsync(message1.Id);

        // Verify only one pending
        var pendingAfterMark = await outbox.GetPendingAsync(10);
        pendingAfterMark.Should().HaveCount(1);
    }

    #region Test Messages

    public record TestScheduledMessage
    {
        public string Id { get; init; } = "";
        public string Content { get; init; } = "";
    }

    public record TestFailedMessage
    {
        public string Id { get; init; } = "";
        public string Reason { get; init; } = "";
    }

    public record TestInboxMessage
    {
        public string Id { get; init; } = "";
        public string Data { get; init; } = "";
    }

    #endregion
}
