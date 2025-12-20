using Catga.Inbox;
using Catga.Resilience;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Persistence;

/// <summary>
/// Comprehensive tests for MemoryInboxStore
/// </summary>
public class MemoryInboxStoreTests
{
    private static MemoryInboxStore CreateStore()
    {
        var provider = Substitute.For<IResiliencePipelineProvider>();
        provider.ExecutePersistenceAsync(Arg.Any<Func<CancellationToken, ValueTask<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var func = callInfo.Arg<Func<CancellationToken, ValueTask<bool>>>();
                return func(CancellationToken.None);
            });
        provider.ExecutePersistenceAsync(Arg.Any<Func<CancellationToken, ValueTask>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var func = callInfo.Arg<Func<CancellationToken, ValueTask>>();
                return func(CancellationToken.None);
            });
        provider.ExecutePersistenceAsync(Arg.Any<Func<CancellationToken, ValueTask<byte[]?>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var func = callInfo.Arg<Func<CancellationToken, ValueTask<byte[]?>>>();
                return func(CancellationToken.None);
            });
        return new MemoryInboxStore(provider);
    }

    [Fact]
    public async Task HasBeenProcessedAsync_WithNewMessage_ShouldReturnFalse()
    {
        var store = CreateStore();

        var result = await store.HasBeenProcessedAsync(123);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasBeenProcessedAsync_WithProcessedMessage_ShouldReturnTrue()
    {
        var store = CreateStore();
        var message = new InboxMessage
        {
            MessageId = 123,
            MessageType = "TestMessage",
            Payload = new byte[] { 1, 2, 3 },
            ProcessingResult = new byte[] { 4, 5, 6 }
        };
        await store.MarkAsProcessedAsync(message);

        var result = await store.HasBeenProcessedAsync(123);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task MarkAsProcessedAsync_ShouldStoreMessage()
    {
        var store = CreateStore();
        var message = new InboxMessage
        {
            MessageId = 123,
            MessageType = "TestMessage",
            Payload = new byte[] { 1, 2, 3 },
            ProcessingResult = new byte[] { 4, 5, 6 },
            CorrelationId = 456
        };

        await store.MarkAsProcessedAsync(message);

        var hasBeenProcessed = await store.HasBeenProcessedAsync(123);
        hasBeenProcessed.Should().BeTrue();
    }

    [Fact]
    public async Task GetProcessedResultAsync_WithProcessedMessage_ShouldReturnResult()
    {
        var store = CreateStore();
        var expectedResult = new byte[] { 4, 5, 6 };
        var message = new InboxMessage
        {
            MessageId = 123,
            MessageType = "TestMessage",
            Payload = new byte[] { 1, 2, 3 },
            ProcessingResult = expectedResult
        };
        await store.MarkAsProcessedAsync(message);

        var result = await store.GetProcessedResultAsync(123);

        result.Should().BeEquivalentTo(expectedResult);
    }

    [Fact]
    public async Task GetProcessedResultAsync_WithNewMessage_ShouldReturnNull()
    {
        var store = CreateStore();

        var result = await store.GetProcessedResultAsync(999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryLockMessageAsync_WithNewMessage_ShouldReturnTrue()
    {
        var store = CreateStore();

        var result = await store.TryLockMessageAsync(123, TimeSpan.FromMinutes(5));

        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryLockMessageAsync_WithLockedMessage_ShouldReturnFalse()
    {
        var store = CreateStore();
        await store.TryLockMessageAsync(123, TimeSpan.FromMinutes(5));

        var result = await store.TryLockMessageAsync(123, TimeSpan.FromMinutes(5));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task TryLockMessageAsync_WithExpiredLock_ShouldReturnTrue()
    {
        var store = CreateStore();
        await store.TryLockMessageAsync(123, TimeSpan.FromMilliseconds(1));
        
        // Wait for lock to expire
        await Task.Delay(50);

        var result = await store.TryLockMessageAsync(123, TimeSpan.FromMinutes(5));

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ReleaseLockAsync_ShouldAllowRelock()
    {
        var store = CreateStore();
        await store.TryLockMessageAsync(123, TimeSpan.FromMinutes(5));

        await store.ReleaseLockAsync(123);

        var result = await store.TryLockMessageAsync(123, TimeSpan.FromMinutes(5));
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ReleaseLockAsync_WithNonExistingLock_ShouldNotThrow()
    {
        var store = CreateStore();

        var act = async () => await store.ReleaseLockAsync(999);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task MultipleMessages_ShouldBeIndependent()
    {
        var store = CreateStore();
        
        await store.MarkAsProcessedAsync(new InboxMessage
        {
            MessageId = 1,
            MessageType = "Type1",
            Payload = new byte[] { 1 },
            ProcessingResult = new byte[] { 10 }
        });
        
        await store.MarkAsProcessedAsync(new InboxMessage
        {
            MessageId = 2,
            MessageType = "Type2",
            Payload = new byte[] { 2 },
            ProcessingResult = new byte[] { 20 }
        });

        var result1 = await store.GetProcessedResultAsync(1);
        var result2 = await store.GetProcessedResultAsync(2);
        var result3 = await store.GetProcessedResultAsync(3);

        result1.Should().BeEquivalentTo(new byte[] { 10 });
        result2.Should().BeEquivalentTo(new byte[] { 20 });
        result3.Should().BeNull();
    }

    [Fact]
    public async Task ConcurrentOperations_ShouldBeThreadSafe()
    {
        var store = CreateStore();
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            var id = i + 1; // MessageId must be > 0
            tasks.Add(Task.Run(async () =>
            {
                await store.MarkAsProcessedAsync(new InboxMessage
                {
                    MessageId = id,
                    MessageType = "TestMessage",
                    Payload = new byte[] { (byte)(id % 256) },
                    ProcessingResult = new byte[] { (byte)((id * 2) % 256) }
                });
            }));
        }

        await Task.WhenAll(tasks);

        // Verify all messages were processed
        for (int i = 1; i <= 100; i++)
        {
            var hasBeenProcessed = await store.HasBeenProcessedAsync(i);
            hasBeenProcessed.Should().BeTrue($"Message {i} should be processed");
        }
    }
}
