using Catga.Abstractions;
using Catga.Idempotency;
using FluentAssertions;
using NSubstitute;
using System.Text;
using Xunit;

namespace Catga.Tests.Core;

public class MemoryIdempotencyStoreTests
{
    private readonly IMessageSerializer _mockSerializer;
    private readonly MemoryIdempotencyStore _store;

    public MemoryIdempotencyStoreTests()
    {
        _mockSerializer = Substitute.For<IMessageSerializer>();

        // Setup default serializer behavior
        _mockSerializer.Serialize(Arg.Any<object>(), Arg.Any<Type>())
            .Returns(callInfo =>
            {
                var obj = callInfo.ArgAt<object>(0);
                return Encoding.UTF8.GetBytes(obj?.ToString() ?? "null");
            });

        _mockSerializer.Deserialize(Arg.Any<byte[]>(), Arg.Any<Type>())
            .Returns(callInfo =>
            {
                var bytes = callInfo.ArgAt<byte[]>(0);
                var text = Encoding.UTF8.GetString(bytes);
                return text;
            });

        _store = new MemoryIdempotencyStore(_mockSerializer);
    }

    // ==================== Constructor Tests ====================

    [Fact]
    public void Constructor_WithNullSerializer_ShouldThrow()
    {
        // Act
        Action act = () => new MemoryIdempotencyStore(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("serializer");
    }

    [Fact]
    public void Constructor_WithValidSerializer_ShouldSucceed()
    {
        // Act
        var store = new MemoryIdempotencyStore(_mockSerializer);

        // Assert
        store.Should().NotBeNull();
    }

    // ==================== HasBeenProcessedAsync Tests ====================

    [Fact]
    public async Task HasBeenProcessedAsync_WithNewMessageId_ShouldReturnFalse()
    {
        // Arrange
        var messageId = 123L;

        // Act
        var result = await _store.HasBeenProcessedAsync(messageId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasBeenProcessedAsync_WithProcessedMessageId_ShouldReturnTrue()
    {
        // Arrange
        var messageId = 456L;
        await _store.MarkAsProcessedAsync<string>(messageId, "test-result");

        // Act
        var result = await _store.HasBeenProcessedAsync(messageId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasBeenProcessedAsync_WithCancellation_ShouldRespectToken()
    {
        // Arrange
        var messageId = 789L;
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = async () => await _store.HasBeenProcessedAsync(messageId, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ==================== MarkAsProcessedAsync Tests ====================

    [Fact]
    public async Task MarkAsProcessedAsync_WithNullResult_ShouldMarkAsProcessed()
    {
        // Arrange
        var messageId = 111L;

        // Act
        await _store.MarkAsProcessedAsync<string>(messageId, null);

        // Assert
        var hasBeenProcessed = await _store.HasBeenProcessedAsync(messageId);
        hasBeenProcessed.Should().BeTrue();
    }

    [Fact]
    public async Task MarkAsProcessedAsync_WithResult_ShouldStoreResult()
    {
        // Arrange
        var messageId = 222L;
        var result = "test-result";

        // Setup serializer to return specific bytes
        var expectedBytes = Encoding.UTF8.GetBytes(result);
        _mockSerializer.Serialize(result, typeof(string)).Returns(expectedBytes);

        // Act
        await _store.MarkAsProcessedAsync(messageId, result);

        // Assert
        _mockSerializer.Received(1).Serialize(result, typeof(string));
    }

    [Fact]
    public async Task MarkAsProcessedAsync_MultipleTimes_ShouldOverwrite()
    {
        // Arrange
        var messageId = 333L;
        await _store.MarkAsProcessedAsync(messageId, "first");

        // Act
        await _store.MarkAsProcessedAsync(messageId, "second");

        // Assert
        var hasBeenProcessed = await _store.HasBeenProcessedAsync(messageId);
        hasBeenProcessed.Should().BeTrue();
    }

    [Fact]
    public async Task MarkAsProcessedAsync_WithCancellation_ShouldRespectToken()
    {
        // Arrange
        var messageId = 444L;
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = async () => await _store.MarkAsProcessedAsync(messageId, "result", cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task MarkAsProcessedAsync_WithComplexType_ShouldSerialize()
    {
        // Arrange
        var messageId = 555L;
        var complexResult = new TestResult { Value = 42, Message = "test" };
        var serializedBytes = Encoding.UTF8.GetBytes("serialized");
        _mockSerializer.Serialize(complexResult, typeof(TestResult)).Returns(serializedBytes);

        // Act
        await _store.MarkAsProcessedAsync(messageId, complexResult);

        // Assert
        _mockSerializer.Received(1).Serialize(complexResult, typeof(TestResult));
    }

    // ==================== GetCachedResultAsync Tests ====================

    [Fact]
    public async Task GetCachedResultAsync_WithNonExistentMessageId_ShouldReturnDefault()
    {
        // Arrange
        var messageId = 666L;

        // Act
        var result = await _store.GetCachedResultAsync<string>(messageId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCachedResultAsync_WithExistingResult_ShouldReturnResult()
    {
        // Arrange
        var messageId = 777L;
        var expectedResult = "cached-result";
        var serializedBytes = Encoding.UTF8.GetBytes(expectedResult);

        _mockSerializer.Serialize(expectedResult, typeof(string)).Returns(serializedBytes);
        _mockSerializer.Deserialize(serializedBytes, typeof(string)).Returns(expectedResult);

        await _store.MarkAsProcessedAsync(messageId, expectedResult);

        // Act
        var result = await _store.GetCachedResultAsync<string>(messageId);

        // Assert
        result.Should().Be(expectedResult);
        _mockSerializer.Received(1).Deserialize(Arg.Any<byte[]>(), typeof(string));
    }

    [Fact]
    public async Task GetCachedResultAsync_WithWrongType_ShouldReturnDefault()
    {
        // Arrange
        var messageId = 888L;
        await _store.MarkAsProcessedAsync(messageId, "string-result");

        // Act - Try to get as wrong type
        var result = await _store.GetCachedResultAsync<int>(messageId);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task GetCachedResultAsync_WithCancellation_ShouldRespectToken()
    {
        // Arrange
        var messageId = 999L;
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = async () => await _store.GetCachedResultAsync<string>(messageId, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ==================== Thread Safety Tests ====================

    [Fact]
    public async Task ConcurrentAccess_ShouldBeThreadSafe()
    {
        // Arrange
        var messageIds = Enumerable.Range(1000, 100).Select(i => (long)i).ToList();

        // Act - Concurrent writes
        var writeTasks = messageIds.Select(id =>
            _store.MarkAsProcessedAsync(id, $"result-{id}")
        ).ToArray();
        await Task.WhenAll(writeTasks);

        // Concurrent reads
        var readTasks = messageIds.Select(id =>
            _store.HasBeenProcessedAsync(id)
        ).ToArray();
        var results = await Task.WhenAll(readTasks);

        // Assert
        results.Should().AllBeEquivalentTo(true);
    }

    [Fact]
    public async Task ConcurrentReadWrite_ShouldNotCorruptState()
    {
        // Arrange
        var messageId = 2000L;
        var tasks = new List<Task>();

        // Act - Mix reads and writes
        for (int i = 0; i < 50; i++)
        {
            tasks.Add(_store.MarkAsProcessedAsync(messageId, $"result-{i}"));
            tasks.Add(_store.HasBeenProcessedAsync(messageId));
            tasks.Add(_store.GetCachedResultAsync<string>(messageId));
        }

        await Task.WhenAll(tasks);

        // Assert - Should complete without exceptions
        var hasBeenProcessed = await _store.HasBeenProcessedAsync(messageId);
        hasBeenProcessed.Should().BeTrue();
    }

    // ==================== Multiple Result Types Tests ====================

    [Fact]
    public async Task MarkAsProcessedAsync_WithMultipleTypes_ShouldStoreSeparately()
    {
        // Arrange
        var messageId = 3000L;
        var stringResult = "string-result";
        var intResult = 42;

        var stringBytes = Encoding.UTF8.GetBytes(stringResult);
        var intBytes = BitConverter.GetBytes(intResult);

        _mockSerializer.Serialize(stringResult, typeof(string)).Returns(stringBytes);
        _mockSerializer.Serialize(intResult, typeof(int)).Returns(intBytes);
        _mockSerializer.Deserialize(stringBytes, typeof(string)).Returns(stringResult);
        _mockSerializer.Deserialize(intBytes, typeof(int)).Returns(intResult);

        // Act
        await _store.MarkAsProcessedAsync(messageId, stringResult);
        await _store.MarkAsProcessedAsync(messageId, intResult);

        // Assert
        var retrievedString = await _store.GetCachedResultAsync<string>(messageId);
        var retrievedInt = await _store.GetCachedResultAsync<int>(messageId);

        retrievedString.Should().Be(stringResult);
        retrievedInt.Should().Be(intResult);
    }

    // ==================== Edge Cases ====================

    [Fact]
    public async Task MarkAsProcessedAsync_WithZeroMessageId_ShouldWork()
    {
        // Arrange
        var messageId = 0L;

        // Act
        await _store.MarkAsProcessedAsync(messageId, "result");

        // Assert
        var hasBeenProcessed = await _store.HasBeenProcessedAsync(messageId);
        hasBeenProcessed.Should().BeTrue();
    }

    [Fact]
    public async Task MarkAsProcessedAsync_WithNegativeMessageId_ShouldWork()
    {
        // Arrange
        var messageId = -1L;

        // Act
        await _store.MarkAsProcessedAsync(messageId, "result");

        // Assert
        var hasBeenProcessed = await _store.HasBeenProcessedAsync(messageId);
        hasBeenProcessed.Should().BeTrue();
    }

    [Fact]
    public async Task MarkAsProcessedAsync_WithMaxMessageId_ShouldWork()
    {
        // Arrange
        var messageId = long.MaxValue;

        // Act
        await _store.MarkAsProcessedAsync(messageId, "result");

        // Assert
        var hasBeenProcessed = await _store.HasBeenProcessedAsync(messageId);
        hasBeenProcessed.Should().BeTrue();
    }

    [Fact]
    public async Task GetCachedResultAsync_BeforeMarkAsProcessed_ShouldReturnDefault()
    {
        // Arrange
        var messageId = 4000L;

        // Act
        var result = await _store.GetCachedResultAsync<string>(messageId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task MultipleOperations_ShouldMaintainConsistency()
    {
        // Arrange & Act
        var messageId1 = 5000L;
        var messageId2 = 5001L;

        await _store.MarkAsProcessedAsync(messageId1, "result1");
        await _store.MarkAsProcessedAsync(messageId2, "result2");

        var has1 = await _store.HasBeenProcessedAsync(messageId1);
        var has2 = await _store.HasBeenProcessedAsync(messageId2);
        var has3 = await _store.HasBeenProcessedAsync(5002L);

        // Assert
        has1.Should().BeTrue();
        has2.Should().BeTrue();
        has3.Should().BeFalse();
    }

    // ==================== Test Helpers ====================

    public class TestResult
    {
        public int Value { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}







