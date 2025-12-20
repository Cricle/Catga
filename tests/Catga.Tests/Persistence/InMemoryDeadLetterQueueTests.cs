using Catga.Abstractions;
using Catga.DeadLetter;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Catga.Tests.Persistence;

/// <summary>
/// Comprehensive tests for InMemoryDeadLetterQueue
/// </summary>
public class InMemoryDeadLetterQueueTests
{
    public record TestMessage(string Data) : IMessage
    {
        public long MessageId { get; init; }
    }

    private static InMemoryDeadLetterQueue CreateQueue(int maxSize = 1000)
    {
        var logger = Substitute.For<ILogger<InMemoryDeadLetterQueue>>();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Serialize(Arg.Any<object>(), Arg.Any<Type>())
            .Returns(callInfo => System.Text.Encoding.UTF8.GetBytes(callInfo.Arg<object>()?.ToString() ?? ""));
        return new InMemoryDeadLetterQueue(logger, serializer, maxSize);
    }

    [Fact]
    public async Task SendAsync_ShouldAddMessage()
    {
        var queue = CreateQueue();
        var message = new TestMessage("test") { MessageId = 123 };
        var exception = new InvalidOperationException("Test failure");

        await queue.SendAsync(message, exception, 1);

        var messages = await queue.GetFailedMessagesAsync();
        messages.Should().HaveCount(1);
        messages.First().MessageId.Should().Be(123);
    }

    [Fact]
    public async Task GetFailedMessagesAsync_WithEmptyQueue_ShouldReturnEmpty()
    {
        var queue = CreateQueue();

        var messages = await queue.GetFailedMessagesAsync();

        messages.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFailedMessagesAsync_ShouldReturnMessages()
    {
        var queue = CreateQueue();
        for (int i = 0; i < 5; i++)
        {
            await queue.SendAsync(
                new TestMessage($"test{i}") { MessageId = i },
                new InvalidOperationException($"Failure {i}"),
                i);
        }

        var messages = await queue.GetFailedMessagesAsync();

        messages.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetFailedMessagesAsync_WithMaxCount_ShouldLimitResults()
    {
        var queue = CreateQueue();
        for (int i = 0; i < 10; i++)
        {
            await queue.SendAsync(
                new TestMessage($"test{i}") { MessageId = i },
                new InvalidOperationException($"Failure {i}"),
                i);
        }

        var messages = await queue.GetFailedMessagesAsync(maxCount: 3);

        messages.Should().HaveCount(3);
    }

    [Fact]
    public async Task SendAsync_ShouldCaptureExceptionDetails()
    {
        var queue = CreateQueue();
        var message = new TestMessage("test") { MessageId = 123 };
        var exception = new InvalidOperationException("Test failure message");

        await queue.SendAsync(message, exception, 3);

        var messages = await queue.GetFailedMessagesAsync();
        var dlm = messages.First();
        dlm.ExceptionType.Should().Be("InvalidOperationException");
        dlm.ExceptionMessage.Should().Be("Test failure message");
        dlm.RetryCount.Should().Be(3);
    }

    [Fact]
    public async Task SendAsync_WithMaxSize_ShouldEvictOldMessages()
    {
        var queue = CreateQueue(maxSize: 5);
        
        for (int i = 0; i < 10; i++)
        {
            await queue.SendAsync(
                new TestMessage($"test{i}") { MessageId = i },
                new InvalidOperationException($"Failure {i}"),
                i);
        }

        var messages = await queue.GetFailedMessagesAsync();
        messages.Should().HaveCount(5);
    }

    [Fact]
    public async Task SendAsync_ShouldSetFailedAt()
    {
        var queue = CreateQueue();
        var message = new TestMessage("test") { MessageId = 123 };
        var beforeSend = DateTime.UtcNow;

        await queue.SendAsync(message, new InvalidOperationException("Test"), 1);

        var messages = await queue.GetFailedMessagesAsync();
        var dlm = messages.First();
        dlm.FailedAt.Should().BeOnOrAfter(beforeSend);
        dlm.FailedAt.Should().BeOnOrBefore(DateTime.UtcNow);
    }

    [Fact]
    public async Task SendAsync_ShouldSetMessageType()
    {
        var queue = CreateQueue();
        var message = new TestMessage("test") { MessageId = 123 };

        await queue.SendAsync(message, new InvalidOperationException("Test"), 1);

        var messages = await queue.GetFailedMessagesAsync();
        messages.First().MessageType.Should().Be("TestMessage");
    }

    [Fact]
    public async Task MultipleOperations_ShouldBeThreadSafe()
    {
        var queue = CreateQueue(maxSize: 1000);
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            var id = i;
            tasks.Add(Task.Run(async () =>
            {
                await queue.SendAsync(
                    new TestMessage($"test{id}") { MessageId = id },
                    new InvalidOperationException($"Failure {id}"),
                    id % 5);
            }));
        }

        await Task.WhenAll(tasks);

        var messages = await queue.GetFailedMessagesAsync(maxCount: 1000);
        messages.Should().HaveCount(100);
    }
}
