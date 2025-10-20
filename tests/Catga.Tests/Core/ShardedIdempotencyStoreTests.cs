using Catga.Idempotency;
using Catga.Serialization.Json;
using FluentAssertions;

namespace Catga.Tests.Core;

/// <summary>
/// ShardedIdempotencyStore unit tests - lock-free high-performance idempotency
/// </summary>
public class ShardedIdempotencyStoreTests
{
    private readonly JsonMessageSerializer _serializer = new();
    [Fact]
    public async Task HasBeenProcessedAsync_NewMessage_ShouldReturnFalse()
    {
        // Arrange
        var store = new ShardedIdempotencyStore(_serializer, shardCount: 4);
        var MessageId = MessageExtensions.NewMessageId();

        // Act
        var result = await store.HasBeenProcessedAsync(MessageId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task MarkAsProcessedAsync_ShouldMarkMessageAsProcessed()
    {
        // Arrange
        var store = new ShardedIdempotencyStore(_serializer, shardCount: 4);
        var MessageId = MessageExtensions.NewMessageId();
        var resultValue = "test result";

        // Act
        await store.MarkAsProcessedAsync(MessageId, resultValue);
        var isProcessed = await store.HasBeenProcessedAsync(MessageId);

        // Assert
        isProcessed.Should().BeTrue();
    }

    [Fact]
    public async Task GetCachedResultAsync_AfterMarkAsProcessed_ShouldReturnResult()
    {
        // Arrange
        var store = new ShardedIdempotencyStore(_serializer, shardCount: 4);
        var MessageId = MessageExtensions.NewMessageId();
        var resultValue = "test result";

        // Act
        await store.MarkAsProcessedAsync(MessageId, resultValue);
        var cachedResult = await store.GetCachedResultAsync<string>(MessageId);

        // Assert
        cachedResult.Should().Be(resultValue);
    }

    [Fact]
    public async Task GetCachedResultAsync_WithoutMarkAsProcessed_ShouldReturnDefault()
    {
        // Arrange
        var store = new ShardedIdempotencyStore(_serializer, shardCount: 4);
        var MessageId = MessageExtensions.NewMessageId();

        // Act
        var cachedResult = await store.GetCachedResultAsync<string>(MessageId);

        // Assert
        cachedResult.Should().BeNull();
    }

    [Fact]
    public async Task MarkAsProcessedAsync_WithNullResult_ShouldStoreExplicitly()
    {
        // Arrange
        var store = new ShardedIdempotencyStore(_serializer, shardCount: 4);
        var MessageId = MessageExtensions.NewMessageId();

        // Act
        await store.MarkAsProcessedAsync<string>(MessageId, null);
        var isProcessed = await store.HasBeenProcessedAsync(MessageId);
        var cachedResult = await store.GetCachedResultAsync<string>(MessageId);

        // Assert
        isProcessed.Should().BeTrue();
        cachedResult.Should().BeNull(); // Explicitly stored null
    }

    [Fact]
    public async Task HasBeenProcessedAsync_ExpiredMessage_ShouldReturnFalse()
    {
        // Arrange
        var retentionPeriod = TimeSpan.FromMilliseconds(100);
        var store = new ShardedIdempotencyStore(_serializer, shardCount: 4, retentionPeriod: retentionPeriod);
        var MessageId = MessageExtensions.NewMessageId();

        // Act
        await store.MarkAsProcessedAsync(MessageId, "test");
        await Task.Delay(150); // Wait for expiration
        var isProcessed = await store.HasBeenProcessedAsync(MessageId);

        // Assert
        isProcessed.Should().BeFalse();
    }

    [Fact]
    public async Task GetCachedResultAsync_ExpiredMessage_ShouldReturnDefault()
    {
        // Arrange
        var retentionPeriod = TimeSpan.FromMilliseconds(100);
        var store = new ShardedIdempotencyStore(_serializer, shardCount: 4, retentionPeriod: retentionPeriod);
        var MessageId = MessageExtensions.NewMessageId();

        // Act
        await store.MarkAsProcessedAsync(MessageId, "test");
        await Task.Delay(150); // Wait for expiration
        var cachedResult = await store.GetCachedResultAsync<string>(MessageId);

        // Assert
        cachedResult.Should().BeNull();
    }

    [Fact]
    public async Task MultipleMessages_ShouldBeDistributedAcrossShards()
    {
        // Arrange
        var store = new ShardedIdempotencyStore(_serializer, shardCount: 8);
        var messageIds = Enumerable.Range(0, 100).Select(_ => MessageExtensions.NewMessageId()).ToArray();

        // Act
        foreach (var MessageId in messageIds)
        {
            await store.MarkAsProcessedAsync(MessageId, $"result-{MessageId}");
        }

        // Assert - All should be processed
        foreach (var MessageId in messageIds)
        {
            var isProcessed = await store.HasBeenProcessedAsync(MessageId);
            isProcessed.Should().BeTrue();
        }
    }

    [Fact]
    public async Task ConcurrentAccess_ShouldBeSafe()
    {
        // Arrange
        var store = new ShardedIdempotencyStore(_serializer, shardCount: 16);
        var messageIds = Enumerable.Range(0, 1000).Select(_ => MessageExtensions.NewMessageId()).ToArray();

        // Act - Concurrent writes
        await Parallel.ForEachAsync(messageIds, async (MessageId, ct) =>
        {
            await store.MarkAsProcessedAsync(MessageId, $"result-{MessageId}", ct);
        });

        // Assert - All should be processed
        foreach (var MessageId in messageIds)
        {
            var isProcessed = await store.HasBeenProcessedAsync(MessageId);
            isProcessed.Should().BeTrue();
        }
    }

    [Fact]
    public async Task DifferentResultTypes_ShouldBeStoredSeparately()
    {
        // Arrange
        var store = new ShardedIdempotencyStore(_serializer, shardCount: 4);
        var MessageId = MessageExtensions.NewMessageId();

        // Act
        await store.MarkAsProcessedAsync(MessageId, "string result");
        await store.MarkAsProcessedAsync(MessageId, 42);

        var stringResult = await store.GetCachedResultAsync<string>(MessageId);
        var intResult = await store.GetCachedResultAsync<int>(MessageId);

        // Assert
        stringResult.Should().Be("string result");
        intResult.Should().Be(42);
    }

    [Fact]
    public void Constructor_WithNonPowerOfTwo_ShouldThrow()
    {
        // Act
        Action act = () => new ShardedIdempotencyStore(_serializer, shardCount: 7);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*power of 2*");
    }

    [Fact]
    public void Constructor_WithZeroShards_ShouldThrow()
    {
        // Act
        Action act = () => new ShardedIdempotencyStore(_serializer, shardCount: 0);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ComplexObject_ShouldBeStoredAndRetrieved()
    {
        // Arrange
        var store = new ShardedIdempotencyStore(_serializer, shardCount: 4);
        var MessageId = MessageExtensions.NewMessageId();
        var complexObject = new TestComplexObject
        {
            Id = 123,
            Name = "Test",
            Data = new List<string> { "A", "B", "C" }
        };

        // Act
        await store.MarkAsProcessedAsync(MessageId, complexObject);
        var retrieved = await store.GetCachedResultAsync<TestComplexObject>(MessageId);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(123);
        retrieved.Name.Should().Be("Test");
        retrieved.Data.Should().BeEquivalentTo(new[] { "A", "B", "C" });
    }

    private class TestComplexObject
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public List<string>? Data { get; set; }
    }
}

