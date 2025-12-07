using Catga.EventSourcing;
using Catga.Persistence.InMemory.Stores;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Unit tests for AuditLogEntry and InMemoryAuditLogStore.
/// </summary>
public class AuditLogTests
{
    [Fact]
    public void AuditLogEntry_Constructor_SetsAllProperties()
    {
        // Act
        var entry = new AuditLogEntry
        {
            StreamId = "stream-1",
            Action = AuditAction.EventAppended,
            UserId = "user-123",
            Details = "Added event",
            Timestamp = DateTime.UtcNow
        };

        // Assert
        entry.StreamId.Should().Be("stream-1");
        entry.Action.Should().Be(AuditAction.EventAppended);
        entry.UserId.Should().Be("user-123");
        entry.Details.Should().Be("Added event");
    }

    [Fact]
    public async Task InMemoryAuditLogStore_LogAsync_StoresEntry()
    {
        // Arrange
        var store = new InMemoryAuditLogStore();
        var entry = new AuditLogEntry
        {
            StreamId = "stream-1",
            Action = AuditAction.StreamRead,
            UserId = "user-1",
            Timestamp = DateTime.UtcNow
        };

        // Act
        await store.LogAsync(entry);
        var logs = await store.GetLogsAsync("stream-1");

        // Assert
        logs.Should().HaveCount(1);
        logs[0].Action.Should().Be(AuditAction.StreamRead);
    }

    [Fact]
    public async Task InMemoryAuditLogStore_GetLogsAsync_ReturnsOnlyMatchingStream()
    {
        // Arrange
        var store = new InMemoryAuditLogStore();
        await store.LogAsync(new AuditLogEntry { StreamId = "stream-1", Action = AuditAction.EventAppended, Timestamp = DateTime.UtcNow });
        await store.LogAsync(new AuditLogEntry { StreamId = "stream-2", Action = AuditAction.StreamRead, Timestamp = DateTime.UtcNow });
        await store.LogAsync(new AuditLogEntry { StreamId = "stream-1", Action = AuditAction.SnapshotCreated, Timestamp = DateTime.UtcNow });

        // Act
        var logs = await store.GetLogsAsync("stream-1");

        // Assert
        logs.Should().HaveCount(2);
        logs.Should().OnlyContain(l => l.StreamId == "stream-1");
    }

    [Fact]
    public async Task InMemoryAuditLogStore_GetLogsByTimeRangeAsync_FiltersCorrectly()
    {
        // Arrange
        var store = new InMemoryAuditLogStore();
        var now = DateTime.UtcNow;

        await store.LogAsync(new AuditLogEntry { StreamId = "s1", Timestamp = now.AddHours(-2), Action = AuditAction.EventAppended });
        await store.LogAsync(new AuditLogEntry { StreamId = "s2", Timestamp = now.AddHours(-1), Action = AuditAction.StreamRead });
        await store.LogAsync(new AuditLogEntry { StreamId = "s3", Timestamp = now, Action = AuditAction.SnapshotCreated });

        // Act
        var logs = await store.GetLogsByTimeRangeAsync(now.AddHours(-1.5), now.AddMinutes(-30));

        // Assert
        logs.Should().HaveCount(1);
        logs[0].StreamId.Should().Be("s2");
    }

    [Fact]
    public async Task InMemoryAuditLogStore_GetLogsByUserAsync_FiltersCorrectly()
    {
        // Arrange
        var store = new InMemoryAuditLogStore();
        await store.LogAsync(new AuditLogEntry { StreamId = "s1", UserId = "user-1", Timestamp = DateTime.UtcNow, Action = AuditAction.EventAppended });
        await store.LogAsync(new AuditLogEntry { StreamId = "s2", UserId = "user-2", Timestamp = DateTime.UtcNow, Action = AuditAction.StreamRead });
        await store.LogAsync(new AuditLogEntry { StreamId = "s3", UserId = "user-1", Timestamp = DateTime.UtcNow, Action = AuditAction.SnapshotCreated });

        // Act
        var logs = await store.GetLogsByUserAsync("user-1");

        // Assert
        logs.Should().HaveCount(2);
        logs.Should().OnlyContain(l => l.UserId == "user-1");
    }

    [Theory]
    [InlineData(AuditAction.EventAppended)]
    [InlineData(AuditAction.StreamRead)]
    [InlineData(AuditAction.SnapshotCreated)]
    [InlineData(AuditAction.SnapshotLoaded)]
    [InlineData(AuditAction.StreamDeleted)]
    [InlineData(AuditAction.DataErased)]
    public async Task InMemoryAuditLogStore_AllAuditActions_Supported(AuditAction action)
    {
        // Arrange
        var store = new InMemoryAuditLogStore();
        var entry = new AuditLogEntry
        {
            StreamId = "test",
            Action = action,
            Timestamp = DateTime.UtcNow
        };

        // Act
        await store.LogAsync(entry);
        var logs = await store.GetLogsAsync("test");

        // Assert
        logs.Should().HaveCount(1);
        logs[0].Action.Should().Be(action);
    }
}
