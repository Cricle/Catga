using Catga.Common;
using FluentAssertions;

namespace Catga.Tests.Core;

/// <summary>
/// BaseMemoryStore unit tests - lock-free base store implementation
/// </summary>
public class BaseMemoryStoreTests
{
    [Fact]
    public void AddOrUpdateMessage_ShouldAddNewMessage()
    {
        // Arrange
        var store = new TestMemoryStore();
        var message = new TestMessage { Id = "1", Data = "test data" };

        // Act
        store.AddOrUpdate(message.Id, message);
        var exists = store.TryGet(message.Id, out var retrieved);

        // Assert
        exists.Should().BeTrue();
        retrieved.Should().NotBeNull();
        retrieved!.Data.Should().Be("test data");
    }

    [Fact]
    public void AddOrUpdateMessage_ShouldUpdateExistingMessage()
    {
        // Arrange
        var store = new TestMemoryStore();
        var message1 = new TestMessage { Id = "1", Data = "original" };
        var message2 = new TestMessage { Id = "1", Data = "updated" };

        // Act
        store.AddOrUpdate(message1.Id, message1);
        store.AddOrUpdate(message2.Id, message2);
        store.TryGet(message1.Id, out var retrieved);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Data.Should().Be("updated");
    }

    [Fact]
    public void TryGetMessage_NonExistent_ShouldReturnFalse()
    {
        // Arrange
        var store = new TestMemoryStore();

        // Act
        var exists = store.TryGet("non-existent", out var message);

        // Assert
        exists.Should().BeFalse();
        message.Should().BeNull();
    }

    [Fact]
    public void GetAllMessages_ShouldReturnAllMessages()
    {
        // Arrange
        var store = new TestMemoryStore();
        store.AddOrUpdate(1L, new TestMessage { Id = "1", Data = "data1" });
        store.AddOrUpdate(2L, new TestMessage { Id = "2", Data = "data2" });
        store.AddOrUpdate(3L, new TestMessage { Id = "3", Data = "data3" });

        // Act
        var allMessages = store.GetAll();

        // Assert
        allMessages.Should().HaveCount(3);
    }

    [Fact]
    public void GetMessagesByPredicate_ShouldFilterCorrectly()
    {
        // Arrange
        var store = new TestMemoryStore();
        store.AddOrUpdate(1L, new TestMessage { Id = "1", Data = "test", Priority = 1 });
        store.AddOrUpdate(2L, new TestMessage { Id = "2", Data = "test", Priority = 2 });
        store.AddOrUpdate(3L, new TestMessage { Id = "3", Data = "other", Priority = 3 });

        // Act
        var filtered = store.GetByPredicate(m => m.Data == "test", maxCount: 10);

        // Assert
        filtered.Should().HaveCount(2);
        filtered.All(m => m.Data == "test").Should().BeTrue();
    }

    [Fact]
    public void GetMessagesByPredicate_WithMaxCount_ShouldLimitResults()
    {
        // Arrange
        var store = new TestMemoryStore();
        for (int i = 0; i < 10; i++)
        {
            store.AddOrUpdate($"msg-{i}", new TestMessage { Id = $"msg-{i}", Data = "test" });
        }

        // Act
        var limited = store.GetByPredicate(m => m.Data == "test", maxCount: 5);

        // Assert
        limited.Should().HaveCount(5);
    }

    [Fact]
    public void GetMessagesByPredicate_WithComparer_ShouldSortCorrectly()
    {
        // Arrange
        var store = new TestMemoryStore();
        store.AddOrUpdate(1L, new TestMessage { Id = "1", Priority = 3 });
        store.AddOrUpdate(2L, new TestMessage { Id = "2", Priority = 1 });
        store.AddOrUpdate(3L, new TestMessage { Id = "3", Priority = 2 });

        var comparer = Comparer<TestMessage>.Create((a, b) => a.Priority.CompareTo(b.Priority));

        // Act
        var sorted = store.GetByPredicate(m => true, maxCount: 10, comparer);

        // Assert
        sorted.Should().HaveCount(3);
        sorted[0].Priority.Should().Be(1);
        sorted[1].Priority.Should().Be(2);
        sorted[2].Priority.Should().Be(3);
    }

    [Fact]
    public void GetCountByPredicate_ShouldCountCorrectly()
    {
        // Arrange
        var store = new TestMemoryStore();
        store.AddOrUpdate(1L, new TestMessage { Id = "1", Data = "test" });
        store.AddOrUpdate(2L, new TestMessage { Id = "2", Data = "test" });
        store.AddOrUpdate(3L, new TestMessage { Id = "3", Data = "other" });

        // Act
        var count = store.GetCountByPredicate(m => m.Data == "test");

        // Assert
        count.Should().Be(2);
    }

    [Fact]
    public async Task DeleteExpiredMessagesAsync_ShouldRemoveExpiredMessages()
    {
        // Arrange
        var store = new TestMemoryStore();
        var oldMessage = new TestMessage { Id = "old", Timestamp = DateTime.UtcNow.AddHours(-2) };
        var recentMessage = new TestMessage { Id = "recent", Timestamp = DateTime.UtcNow };

        store.AddOrUpdate(oldMessage.Id, oldMessage);
        store.AddOrUpdate(recentMessage.Id, recentMessage);

        // Act
        await store.DeleteExpiredAsync(
            retentionPeriod: TimeSpan.FromHours(1),
            predicate: m => m.Timestamp < DateTime.UtcNow.AddHours(-1));

        // Assert
        store.TryGet("old", out _).Should().BeFalse();
        store.TryGet("recent", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteIfExistsAsync_WhenExists_ShouldExecuteAction()
    {
        // Arrange
        var store = new TestMemoryStore();
        var message = new TestMessage { Id = "1", Data = "original" };
        store.AddOrUpdate(message.Id, message);

        // Act
        await store.ExecuteIfExistsPublic(message.Id, m => m.Data = "modified");
        store.TryGet(message.Id, out var retrieved);

        // Assert
        retrieved!.Data.Should().Be("modified");
    }

    [Fact]
    public async Task ExecuteIfExistsAsync_WhenNotExists_ShouldNotThrow()
    {
        // Arrange
        var store = new TestMemoryStore();

        // Act
        Func<Task> act = async () => await store.ExecuteIfExistsPublic("non-existent", m => m.Data = "test");

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetValueIfExistsAsync_WhenExists_ShouldReturnValue()
    {
        // Arrange
        var store = new TestMemoryStore();
        var message = new TestMessage { Id = "1", Data = "test data" };
        store.AddOrUpdate(message.Id, message);

        // Act
        var result = await store.GetValueIfExistsPublic(message.Id, m => m.Data);

        // Assert
        result.Should().Be("test data");
    }

    [Fact]
    public async Task GetValueIfExistsAsync_WhenNotExists_ShouldReturnDefault()
    {
        // Arrange
        var store = new TestMemoryStore();

        // Act
        var result = await store.GetValueIfExistsPublic("non-existent", m => m.Data);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ConcurrentAccess_ShouldBeSafe()
    {
        // Arrange
        var store = new TestMemoryStore();
        var messages = Enumerable.Range(0, 1000).Select(i =>
            new TestMessage { Id = $"msg-{i}", Data = $"data-{i}" }).ToArray();

        // Act - Concurrent writes
        Parallel.ForEach(messages, message =>
        {
            store.AddOrUpdate(message.Id, message);
        });

        // Assert - All messages should be stored
        foreach (var message in messages)
        {
            store.TryGet(message.Id, out var retrieved).Should().BeTrue();
            retrieved!.Data.Should().Be(message.Data);
        }
    }

    // Test implementation of BaseMemoryStore
    private class TestMemoryStore : BaseMemoryStore<TestMessage>
    {
        private long StringToLong(string id) => long.TryParse(id, out var result) ? result : id.GetHashCode();

        public void AddOrUpdate(string id, TestMessage message) => AddOrUpdateMessage(StringToLong(id), message);
        public void AddOrUpdate(long id, TestMessage message) => AddOrUpdateMessage(id, message);
        public bool TryGet(string id, out TestMessage? message) => TryGetMessage(StringToLong(id), out message);
        public List<TestMessage> GetAll() => Messages.Values.ToList();
        public List<TestMessage> GetByPredicate(Func<TestMessage, bool> predicate, int maxCount, IComparer<TestMessage>? comparer = null)
            => GetMessagesByPredicate(predicate, maxCount, comparer);
        public new int GetCountByPredicate(Func<TestMessage, bool> predicate) => base.GetCountByPredicate(predicate);
        public ValueTask DeleteExpiredAsync(TimeSpan retentionPeriod, Func<TestMessage, bool> predicate, CancellationToken ct = default)
            => DeleteMessagesByPredicateAsync(predicate, ct);

        public ValueTask DeleteExpiredWithTimestampAsync(TimeSpan retentionPeriod, Func<TestMessage, DateTime?> timestampSelector, Func<TestMessage, bool> statusFilter, CancellationToken ct = default)
            => DeleteExpiredMessagesAsync(retentionPeriod, timestampSelector, statusFilter, ct);
        public Task ExecuteIfExistsPublic(string id, Action<TestMessage> action) => ExecuteIfExistsAsync(StringToLong(id), action);
        public Task<TResult?> GetValueIfExistsPublic<TResult>(string id, Func<TestMessage, TResult?> selector)
            => GetValueIfExistsAsync(StringToLong(id), selector);
    }

    private class TestMessage
    {
        public string Id { get; set; } = string.Empty;
        public string? Data { get; set; }
        public int Priority { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}

