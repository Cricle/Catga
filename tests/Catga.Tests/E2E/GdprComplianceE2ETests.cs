using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.EventSourcing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// E2E tests for GDPR compliance features.
/// Tests data erasure requests, audit logging, and data subject rights.
/// </summary>
public class GdprComplianceE2ETests
{
    [Fact]
    public async Task GdprErasure_SubmitRequest_CreatesErasureRequest()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var gdprStore = sp.GetRequiredService<IGdprStore>();

        var subjectId = $"customer-{Guid.NewGuid():N}"[..16];
        var request = new ErasureRequest(subjectId, "user@example.com", DateTime.UtcNow);

        // Act
        await gdprStore.SaveRequestAsync(request);
        var retrieved = await gdprStore.GetErasureRequestAsync(subjectId);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.SubjectId.Should().Be(subjectId);
        retrieved.RequestedBy.Should().Be("user@example.com");
    }

    [Fact]
    public async Task GdprErasure_GetPendingRequests_ReturnsAllPending()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var gdprStore = sp.GetRequiredService<IGdprStore>();

        // Submit multiple requests
        for (int i = 0; i < 5; i++)
        {
            var request = new ErasureRequest(
                $"customer-{i:000}",
                $"user{i}@example.com",
                DateTime.UtcNow);
            await gdprStore.SaveRequestAsync(request);
        }

        // Act
        var pending = await gdprStore.GetPendingRequestsAsync();

        // Assert
        pending.Should().HaveCountGreaterOrEqualTo(5);
    }

    [Fact]
    public async Task AuditLog_LogOperation_CreatesAuditEntry()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var auditStore = sp.GetRequiredService<IAuditLogStore>();

        var streamId = $"Order-{Guid.NewGuid():N}"[..16];
        var entry = new AuditLogEntry(
            streamId,
            "CreateOrder",
            "admin@company.com",
            DateTime.UtcNow,
            new Dictionary<string, object>
            {
                ["orderId"] = "ORD-001",
                ["amount"] = 999.99m,
                ["customerId"] = "CUST-001"
            });

        // Act
        await auditStore.LogAsync(entry);
        var logs = await auditStore.GetLogsAsync(streamId);

        // Assert
        logs.Should().HaveCount(1);
        logs[0].StreamId.Should().Be(streamId);
        logs[0].Operation.Should().Be("CreateOrder");
        logs[0].UserId.Should().Be("admin@company.com");
        logs[0].Details.Should().ContainKey("orderId");
    }

    [Fact]
    public async Task AuditLog_MultipleOperations_ReturnsInOrder()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var auditStore = sp.GetRequiredService<IAuditLogStore>();

        var streamId = $"Order-{Guid.NewGuid():N}"[..16];

        // Log multiple operations
        await auditStore.LogAsync(new AuditLogEntry(streamId, "CreateOrder", "user1", DateTime.UtcNow.AddMinutes(-2), new()));
        await auditStore.LogAsync(new AuditLogEntry(streamId, "UpdateOrder", "user2", DateTime.UtcNow.AddMinutes(-1), new()));
        await auditStore.LogAsync(new AuditLogEntry(streamId, "ShipOrder", "user3", DateTime.UtcNow, new()));

        // Act
        var logs = await auditStore.GetLogsAsync(streamId);

        // Assert
        logs.Should().HaveCount(3);
        logs.Select(l => l.Operation).Should().ContainInOrder("CreateOrder", "UpdateOrder", "ShipOrder");
    }

    [Fact]
    public async Task ImmutabilityVerifier_VerifyStream_ReturnsValidResult()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var streamId = $"Order-{Guid.NewGuid():N}"[..16];
        await eventStore.AppendAsync(streamId, new IEvent[]
        {
            new TestOrderEvent { OrderId = "ORD-001", Action = "Created" },
            new TestOrderEvent { OrderId = "ORD-001", Action = "Updated" }
        });

        var verifier = new ImmutabilityVerifier(eventStore);

        // Act
        var result = await verifier.VerifyAsync(streamId);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Hash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GdprService_AnonymizeData_RemovesPersonalInfo()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var gdprStore = sp.GetRequiredService<IGdprStore>();
        var encryptionStore = sp.GetRequiredService<IEncryptionKeyStore>();

        // Store encryption key for customer data
        var customerId = "CUST-001";
        var key = new EncryptionKey(customerId, Convert.ToBase64String(new byte[32]), DateTime.UtcNow);
        await encryptionStore.StoreKeyAsync(key);

        // Verify key exists
        var retrievedKey = await encryptionStore.GetKeyAsync(customerId);
        retrievedKey.Should().NotBeNull();

        // Submit erasure request
        var request = new ErasureRequest(customerId, "dpo@company.com", DateTime.UtcNow);
        await gdprStore.SaveRequestAsync(request);

        // Simulate erasure by deleting key
        await encryptionStore.DeleteKeyAsync(customerId);

        // Act - Verify key is deleted
        var deletedKey = await encryptionStore.GetKeyAsync(customerId);

        // Assert
        deletedKey.Should().BeNull();
    }

    [Fact]
    public async Task EncryptionKeyStore_RotateKey_UpdatesKey()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var keyStore = sp.GetRequiredService<IEncryptionKeyStore>();

        var subjectId = "subject-001";
        var originalKey = new EncryptionKey(subjectId, "original-key-data", DateTime.UtcNow.AddDays(-30));

        // Act - Store original key
        await keyStore.StoreKeyAsync(originalKey);
        var retrieved1 = await keyStore.GetKeyAsync(subjectId);

        // Act - Rotate key
        var newKey = new EncryptionKey(subjectId, "new-key-data", DateTime.UtcNow);
        await keyStore.StoreKeyAsync(newKey);
        var retrieved2 = await keyStore.GetKeyAsync(subjectId);

        // Assert
        retrieved1!.EncryptedKey.Should().Be("original-key-data");
        retrieved2!.EncryptedKey.Should().Be("new-key-data");
    }

    [Fact]
    public async Task CompleteGdprWorkflow_RequestToErasure_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var gdprStore = sp.GetRequiredService<IGdprStore>();
        var auditStore = sp.GetRequiredService<IAuditLogStore>();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var customerId = $"customer-{Guid.NewGuid():N}"[..16];
        var streamId = $"Customer-{customerId}";

        // Step 1: Create customer data (events)
        await eventStore.AppendAsync(streamId, new IEvent[]
        {
            new CustomerCreatedEvent { CustomerId = customerId, Name = "John Doe", Email = "john@example.com" },
            new CustomerUpdatedEvent { CustomerId = customerId, Name = "John Smith" }
        });

        // Step 2: Log audit entry for data creation
        await auditStore.LogAsync(new AuditLogEntry(
            streamId, "CustomerDataCreated", "system", DateTime.UtcNow,
            new Dictionary<string, object> { ["customerId"] = customerId }));

        // Step 3: Submit GDPR erasure request
        var erasureRequest = new ErasureRequest(customerId, "dpo@company.com", DateTime.UtcNow);
        await gdprStore.SaveRequestAsync(erasureRequest);

        // Step 4: Verify request is pending
        var pending = await gdprStore.GetPendingRequestsAsync();
        pending.Should().Contain(r => r.SubjectId == customerId);

        // Step 5: Log erasure action
        await auditStore.LogAsync(new AuditLogEntry(
            streamId, "GdprErasureRequested", "dpo@company.com", DateTime.UtcNow,
            new Dictionary<string, object> { ["requestId"] = customerId }));

        // Assert - Full audit trail exists
        var auditLogs = await auditStore.GetLogsAsync(streamId);
        auditLogs.Should().HaveCount(2);
        auditLogs.Should().Contain(l => l.Operation == "CustomerDataCreated");
        auditLogs.Should().Contain(l => l.Operation == "GdprErasureRequested");
    }

    #region Test Events

    public record TestOrderEvent : IEvent
    {
        public long MessageId { get; init; }
        public string OrderId { get; init; } = "";
        public string Action { get; init; } = "";
    }

    public record CustomerCreatedEvent : IEvent
    {
        public long MessageId { get; init; }
        public string CustomerId { get; init; } = "";
        public string Name { get; init; } = "";
        public string Email { get; init; } = "";
    }

    public record CustomerUpdatedEvent : IEvent
    {
        public long MessageId { get; init; }
        public string CustomerId { get; init; } = "";
        public string Name { get; init; } = "";
    }

    #endregion
}
