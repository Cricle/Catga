using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.Stores;
using Catga.Persistence.InMemory.Stores;
using Catga.Resilience;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// TDD tests for audit and compliance functionality.
/// </summary>
public class AuditTests
{
    private readonly InMemoryEventStore _eventStore;
    private readonly InMemoryAuditLogStore _auditStore;

    public AuditTests()
    {
        _eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        _auditStore = new InMemoryAuditLogStore();
    }

    #region 1. Immutability verification

    [Fact]
    public async Task ImmutabilityVerifier_DetectsUnmodifiedStream()
    {
        // Arrange
        var streamId = "Order-order-1";
        await _eventStore.AppendAsync(streamId, [
            new TestEvent { Data = "event-1" },
            new TestEvent { Data = "event-2" }
        ]);

        var verifier = new ImmutabilityVerifier(_eventStore);

        // Act
        var result = await verifier.VerifyStreamAsync(streamId);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Hash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ImmutabilityVerifier_GeneratesConsistentHash()
    {
        // Arrange
        var streamId = "Order-order-1";
        await _eventStore.AppendAsync(streamId, [
            new TestEvent { Data = "event-1" },
            new TestEvent { Data = "event-2" }
        ]);

        var verifier = new ImmutabilityVerifier(_eventStore);

        // Act
        var result1 = await verifier.VerifyStreamAsync(streamId);
        var result2 = await verifier.VerifyStreamAsync(streamId);

        // Assert
        result1.Hash.Should().Be(result2.Hash);
    }

    [Fact]
    public async Task ImmutabilityVerifier_DetectsHashMismatch()
    {
        // Arrange
        var streamId = "Order-order-1";
        await _eventStore.AppendAsync(streamId, [new TestEvent { Data = "event-1" }]);

        var verifier = new ImmutabilityVerifier(_eventStore);
        var originalResult = await verifier.VerifyStreamAsync(streamId);

        // Act - Verify against wrong hash
        var result = await verifier.VerifyAgainstHashAsync(streamId, "wrong-hash");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("mismatch");
    }

    #endregion

    #region 2. Audit logging

    [Fact]
    public async Task AuditLog_RecordsEventAppend()
    {
        // Arrange
        var entry = new AuditLogEntry
        {
            Action = AuditAction.EventAppended,
            StreamId = "Order-order-1",
            UserId = "user-1",
            Timestamp = DateTime.UtcNow,
            Details = "Appended 2 events"
        };

        // Act
        await _auditStore.LogAsync(entry);

        // Assert
        var logs = await _auditStore.GetLogsAsync("Order-order-1");
        logs.Should().HaveCount(1);
        logs[0].Action.Should().Be(AuditAction.EventAppended);
    }

    [Fact]
    public async Task AuditLog_QueriesByTimeRange()
    {
        // Arrange
        var now = DateTime.UtcNow;
        await _auditStore.LogAsync(new AuditLogEntry
        {
            Action = AuditAction.EventAppended,
            StreamId = "Order-order-1",
            UserId = "user-1",
            Timestamp = now.AddHours(-2)
        });
        await _auditStore.LogAsync(new AuditLogEntry
        {
            Action = AuditAction.StreamRead,
            StreamId = "Order-order-1",
            UserId = "user-2",
            Timestamp = now.AddHours(-1)
        });
        await _auditStore.LogAsync(new AuditLogEntry
        {
            Action = AuditAction.EventAppended,
            StreamId = "Order-order-2",
            UserId = "user-1",
            Timestamp = now
        });

        // Act
        var logs = await _auditStore.GetLogsByTimeRangeAsync(now.AddHours(-1.5), now.AddMinutes(-30));

        // Assert
        logs.Should().HaveCount(1);
        logs[0].Action.Should().Be(AuditAction.StreamRead);
    }

    [Fact]
    public async Task AuditLog_QueriesByUser()
    {
        // Arrange
        await _auditStore.LogAsync(new AuditLogEntry
        {
            Action = AuditAction.EventAppended,
            StreamId = "Order-order-1",
            UserId = "user-1",
            Timestamp = DateTime.UtcNow
        });
        await _auditStore.LogAsync(new AuditLogEntry
        {
            Action = AuditAction.StreamRead,
            StreamId = "Order-order-2",
            UserId = "user-2",
            Timestamp = DateTime.UtcNow
        });

        // Act
        var logs = await _auditStore.GetLogsByUserAsync("user-1");

        // Assert
        logs.Should().HaveCount(1);
        logs[0].StreamId.Should().Be("Order-order-1");
    }

    #endregion

    #region 3. GDPR support (data erasure)

    [Fact]
    public async Task GdprService_MarksStreamForErasure()
    {
        // Arrange
        var gdprStore = new InMemoryGdprStore();
        var service = new GdprService(gdprStore);

        // Act
        await service.RequestErasureAsync("customer-123", "user-1");

        // Assert
        var request = await gdprStore.GetErasureRequestAsync("customer-123");
        request.Should().NotBeNull();
        request!.Status.Should().Be(ErasureStatus.Pending);
    }

    [Fact]
    public async Task GdprService_TracksErasureCompletion()
    {
        // Arrange
        var gdprStore = new InMemoryGdprStore();
        var service = new GdprService(gdprStore);
        await service.RequestErasureAsync("customer-123", "user-1");

        // Act
        await service.CompleteErasureAsync("customer-123", ["Order-order-1", "Order-order-2"]);

        // Assert
        var request = await gdprStore.GetErasureRequestAsync("customer-123");
        request!.Status.Should().Be(ErasureStatus.Completed);
        request.AffectedStreams.Should().HaveCount(2);
    }

    [Fact]
    public async Task GdprService_ListsPendingRequests()
    {
        // Arrange
        var gdprStore = new InMemoryGdprStore();
        var service = new GdprService(gdprStore);
        await service.RequestErasureAsync("customer-1", "user-1");
        await service.RequestErasureAsync("customer-2", "user-1");
        await service.CompleteErasureAsync("customer-1", []);

        // Act
        var pending = await service.GetPendingRequestsAsync();

        // Assert
        pending.Should().HaveCount(1);
        pending[0].SubjectId.Should().Be("customer-2");
    }

    [Fact]
    public async Task CryptoErasure_EncryptsAndDestroysKey()
    {
        // Arrange
        var keyStore = new InMemoryEncryptionKeyStore();
        var cryptoService = new CryptoErasureService(keyStore);

        // Create key for subject
        var key = await cryptoService.GetOrCreateKeyAsync("customer-123");
        key.Should().NotBeNullOrEmpty();

        // Act - Destroy key (crypto erasure)
        await cryptoService.DestroyKeyAsync("customer-123");

        // Assert
        var retrievedKey = await keyStore.GetKeyAsync("customer-123");
        retrievedKey.Should().BeNull();
    }

    #endregion

    #region Test helpers

    private record TestEvent : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
        public string Data { get; init; } = "";
    }

    #endregion
}
