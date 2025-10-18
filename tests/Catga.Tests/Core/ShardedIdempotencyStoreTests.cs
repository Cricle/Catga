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
        var messageId = Guid.NewGuid().ToString();

        // Act
        var result = await store.HasBeenProcessedAsync(messageId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task MarkAsProcessedAsync_ShouldMarkMessageAsProcessed()
    {
        // Arrange
        var store = new ShardedIdempotencyStore(_serializer, shardCount: 4);
        var messageId = Guid.NewGuid().ToString();
        var resultValue = "test result";

        // Act
        await store.MarkAsProcessedAsync(messageId, resultValue);
        var isProcessed = await store.HasBeenProcessedAsync(messageId);

        // Assert
        isProcessed.Should().BeTrue();
    }

    [Fact]
    public async Task GetCachedResultAsync_AfterMarkAsProcessed_ShouldReturnResult()
    {
        // Arrange
        var store = new ShardedIdempotencyStore(_serializer, shardCount: 4);
        var messageId = Guid.NewGuid().ToString();
        var resultValue = "test result";

        // Act
        await store.MarkAsProcessedAsync(messageId, resultValue);
        var cachedResult = await store.GetCachedResultAsync<string>(messageId);

        // Assert
        cachedResult.Should().Be(resultValue);
    }

    [Fact]
    public async Task GetCachedResultAsync_WithoutMarkAsProcessed_ShouldReturnDefault()
    {
        // Arrange
        var store = new ShardedIdempotencyStore(_serializer, shardCount: 4);
        var messageId = Guid.NewGuid().ToString();

        // Act
        var cachedResult = await store.GetCachedResultAsync<string>(messageId);

        // Assert
        cachedResult.Should().BeNull();
    }

    [Fact]
    public async Task MarkAsProcessedAsync_WithNullResult_ShouldStoreExplicitly()
    {
        // Arrange
        var store = new ShardedIdempotencyStore(_serializer, shardCount: 4);
        var messageId = Guid.NewGuid().ToString();

        // Act
        await store.MarkAsProcessedAsync<string>(messageId, null);
        var isProcessed = await store.HasBeenProcessedAsync(messageId);
        var cachedResult = await store.GetCachedResultAsync<string>(messageId);

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
        var messageId = Guid.NewGuid().ToString();

        // Act
        await store.MarkAsProcessedAsync(messageId, "test");
        await Task.Delay(150); // Wait for expiration
        var isProcessed = await store.HasBeenProcessedAsync(messageId);

        // Assert
        isProcessed.Should().BeFalse();
    }

    [Fact]
    public async Task GetCachedResultAsync_ExpiredMessage_ShouldReturnDefault()
    {
        // Arrange
        var retentionPeriod = TimeSpan.FromMilliseconds(100);
        var store = new ShardedIdempotencyStore(_serializer, shardCount: 4, retentionPeriod: retentionPeriod);
        var messageId = Guid.NewGuid().ToString();

        // Act
        await store.MarkAsProcessedAsync(messageId, "test");
        await Task.Delay(150); // Wait for expiration
        var cachedResult = await store.GetCachedResultAsync<string>(messageId);

        // Assert
        cachedResult.Should().BeNull();
    }

    [Fact]
    public async Task MultipleMessages_ShouldBeDistributedAcrossShards()
    {
        // Arrange
        var store = new ShardedIdempotencyStore(_serializer, shardCount: 8);
        var messageIds = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid().ToString()).ToArray();

        // Act
        foreach (var messageId in messageIds)
        {
            await store.MarkAsProcessedAsync(messageId, $"result-{messageId}");
        }

        // Assert - All should be processed
        foreach (var messageId in messageIds)
        {
            var isProcessed = await store.HasBeenProcessedAsync(messageId);
            isProcessed.Should().BeTrue();
        }
    }

    [Fact]
    public async Task ConcurrentAccess_ShouldBeSafe()
    {
        // Arrange
        var store = new ShardedIdempotencyStore(_serializer, shardCount: 16);
        var messageIds = Enumerable.Range(0, 1000).Select(_ => Guid.NewGuid().ToString()).ToArray();

        // Act - Concurrent writes
        await Parallel.ForEachAsync(messageIds, async (messageId, ct) =>
        {
            await store.MarkAsProcessedAsync(messageId, $"result-{messageId}", ct);
        });

        // Assert - All should be processed
        foreach (var messageId in messageIds)
        {
            var isProcessed = await store.HasBeenProcessedAsync(messageId);
            isProcessed.Should().BeTrue();
        }
    }

    [Fact]
    public async Task DifferentResultTypes_ShouldBeStoredSeparately()
    {
        // Arrange
        var store = new ShardedIdempotencyStore(_serializer, shardCount: 4);
        var messageId = Guid.NewGuid().ToString();

        // Act
        await store.MarkAsProcessedAsync(messageId, "string result");
        await store.MarkAsProcessedAsync(messageId, 42);

        var stringResult = await store.GetCachedResultAsync<string>(messageId);
        var intResult = await store.GetCachedResultAsync<int>(messageId);

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
        var messageId = Guid.NewGuid().ToString();
        var complexObject = new TestComplexObject
        {
            Id = 123,
            Name = "Test",
            Data = new List<string> { "A", "B", "C" }
        };

        // Act
        await store.MarkAsProcessedAsync(messageId, complexObject);
        var retrieved = await store.GetCachedResultAsync<TestComplexObject>(messageId);

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

