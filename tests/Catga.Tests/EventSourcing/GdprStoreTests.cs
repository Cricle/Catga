using Catga.EventSourcing;
using Catga.Persistence.InMemory.Stores;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Unit tests for GDPR stores and GdprService.
/// </summary>
public class GdprStoreTests
{
    [Fact]
    public async Task InMemoryGdprStore_SaveRequestAsync_StoresRequest()
    {
        // Arrange
        var store = new InMemoryGdprStore();
        var request = new ErasureRequest
        {
            Id = Guid.NewGuid().ToString(),
            SubjectId = "customer-123",
            RequestedBy = "admin",
            RequestedAt = DateTime.UtcNow,
            Status = ErasureStatus.Pending
        };

        // Act
        await store.SaveRequestAsync(request);
        var loaded = await store.GetErasureRequestAsync("customer-123");

        // Assert
        loaded.Should().NotBeNull();
        loaded!.SubjectId.Should().Be("customer-123");
        loaded.RequestedBy.Should().Be("admin");
    }

    [Fact]
    public async Task InMemoryGdprStore_GetPendingRequestsAsync_ReturnsOnlyPending()
    {
        // Arrange
        var store = new InMemoryGdprStore();
        await store.SaveRequestAsync(new ErasureRequest
        {
            Id = "1", SubjectId = "c1", RequestedBy = "admin", RequestedAt = DateTime.UtcNow, Status = ErasureStatus.Pending
        });
        await store.SaveRequestAsync(new ErasureRequest
        {
            Id = "2", SubjectId = "c2", RequestedBy = "admin", RequestedAt = DateTime.UtcNow, Status = ErasureStatus.Completed
        });

        // Act
        var pending = await store.GetPendingRequestsAsync();

        // Assert
        pending.Should().HaveCount(1);
        pending[0].SubjectId.Should().Be("c1");
    }

    [Fact]
    public async Task GdprService_RequestErasureAsync_CreatesRequest()
    {
        // Arrange
        var store = new InMemoryGdprStore();
        var service = new GdprService(store);

        // Act
        await service.RequestErasureAsync("customer-123", "admin");
        var requests = await store.GetPendingRequestsAsync();

        // Assert
        requests.Should().HaveCount(1);
        requests[0].SubjectId.Should().Be("customer-123");
    }

    [Fact]
    public async Task GdprService_GetPendingRequestsAsync_ReturnsRequests()
    {
        // Arrange
        var store = new InMemoryGdprStore();
        var service = new GdprService(store);
        await service.RequestErasureAsync("customer-1", "admin");
        await service.RequestErasureAsync("customer-2", "admin");

        // Act
        var pending = await service.GetPendingRequestsAsync();

        // Assert
        pending.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(ErasureStatus.Pending)]
    [InlineData(ErasureStatus.InProgress)]
    [InlineData(ErasureStatus.Completed)]
    [InlineData(ErasureStatus.Failed)]
    public void ErasureStatus_AllValuesValid(ErasureStatus status)
    {
        // Act
        var request = new ErasureRequest
        {
            Id = Guid.NewGuid().ToString(),
            SubjectId = "test",
            RequestedBy = "admin",
            RequestedAt = DateTime.UtcNow,
            Status = status
        };

        // Assert
        request.Status.Should().Be(status);
    }

    [Fact]
    public void ErasureRequest_Properties_SetCorrectly()
    {
        // Arrange
        var now = DateTime.UtcNow;

        // Act
        var request = new ErasureRequest
        {
            Id = "req-1",
            SubjectId = "customer-123",
            RequestedBy = "admin",
            RequestedAt = now,
            Status = ErasureStatus.Pending
        };

        // Assert
        request.Id.Should().Be("req-1");
        request.SubjectId.Should().Be("customer-123");
        request.RequestedBy.Should().Be("admin");
        request.RequestedAt.Should().Be(now);
        request.Status.Should().Be(ErasureStatus.Pending);
    }
}
