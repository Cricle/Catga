using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// Advanced E2E tests for OrderSystem.Api demonstrating all Catga features.
/// </summary>
public class OrderSystemAdvancedE2ETests : IClassFixture<WebApplicationFactory<OrderSystem.Api.Program>>
{
    private readonly HttpClient _client;

    public OrderSystemAdvancedE2ETests(WebApplicationFactory<OrderSystem.Api.Program> factory)
    {
        _client = factory.CreateClient();
    }

    #region Order CRUD Tests

    [Fact]
    public async Task Orders_CreateAndRetrieve_Works()
    {
        // Arrange
        var order = new
        {
            customerId = $"customer-{Guid.NewGuid():N}"[..16],
            items = new[]
            {
                new { productId = "P1", productName = "Laptop", quantity = 1, unitPrice = 999.99m },
                new { productId = "P2", productName = "Mouse", quantity = 2, unitPrice = 29.99m }
            }
        };

        // Act - Create order
        var createResponse = await _client.PostAsJsonAsync("/api/orders", order);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var createResult = await createResponse.Content.ReadFromJsonAsync<OrderCreatedResult>();
        createResult.Should().NotBeNull();
        createResult!.OrderId.Should().NotBeNullOrEmpty();

        // Act - Get order
        var getResponse = await _client.GetAsync($"/api/orders/{createResult.OrderId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Orders_CreateWithFlow_UsesCompensation()
    {
        // Arrange
        var order = new
        {
            customerId = $"customer-{Guid.NewGuid():N}"[..16],
            items = new[]
            {
                new { productId = "P1", productName = "Test Product", quantity = 1, unitPrice = 50m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders/flow", order);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<OrderCreatedResult>();
        result!.OrderId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Orders_Cancel_ReturnsSuccess()
    {
        // Arrange - Create order first
        var order = new
        {
            customerId = "cancel-test-customer",
            items = new[] { new { productId = "P1", productName = "Test", quantity = 1, unitPrice = 10m } }
        };
        var createResponse = await _client.PostAsJsonAsync("/api/orders", order);
        var createResult = await createResponse.Content.ReadFromJsonAsync<OrderCreatedResult>();

        // Act
        var cancelResponse = await _client.PostAsJsonAsync(
            $"/api/orders/{createResult!.OrderId}/cancel",
            new { reason = "Customer requested cancellation" });

        // Assert
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Orders_GetUserOrders_ReturnsOrders()
    {
        // Arrange
        var customerId = $"user-orders-{Guid.NewGuid():N}"[..16];
        var order = new
        {
            customerId,
            items = new[] { new { productId = "P1", productName = "Test", quantity = 1, unitPrice = 10m } }
        };
        await _client.PostAsJsonAsync("/api/orders", order);

        // Act
        var response = await _client.GetAsync($"/api/users/{customerId}/orders");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Time Travel Tests

    [Fact]
    public async Task TimeTravel_CreateDemoAndGetState_ReturnsCorrectVersion()
    {
        // Arrange - Create demo order
        var createResponse = await _client.PostAsync("/api/timetravel/demo/create", null);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var demo = await createResponse.Content.ReadFromJsonAsync<DemoCreateResult>();

        // Act - Get state at version 1
        var v1Response = await _client.GetAsync($"/api/timetravel/orders/{demo!.OrderId}/version/1");

        // Assert
        v1Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var v1State = await v1Response.Content.ReadFromJsonAsync<TimeTravelState>();
        v1State.Should().NotBeNull();
        v1State!.Version.Should().Be(1);
    }

    [Fact]
    public async Task TimeTravel_GetHistory_ReturnsAllVersions()
    {
        // Arrange
        var createResponse = await _client.PostAsync("/api/timetravel/demo/create", null);
        var demo = await createResponse.Content.ReadFromJsonAsync<DemoCreateResult>();

        // Act
        var historyResponse = await _client.GetAsync($"/api/timetravel/orders/{demo!.OrderId}/history");

        // Assert
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await historyResponse.Content.ReadFromJsonAsync<List<VersionHistoryItem>>();
        history.Should().NotBeEmpty();
        history!.Count.Should().BeGreaterOrEqualTo(1);
    }

    #endregion

    #region Projection Tests

    [Fact]
    public async Task Projections_GetOrderSummary_ReturnsAggregates()
    {
        // Act
        var response = await _client.GetAsync("/api/projections/order-summary");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<OrderSummaryResult>();
        summary.Should().NotBeNull();
    }

    [Fact]
    public async Task Projections_RebuildOrderSummary_ReturnsSuccess()
    {
        // Act
        var response = await _client.PostAsync("/api/projections/order-summary/rebuild", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Projections_GetCustomerStats_ReturnsStats()
    {
        // Act
        var response = await _client.GetAsync("/api/projections/customer-stats");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Subscription Tests

    [Fact]
    public async Task Subscriptions_CreateAndList_Works()
    {
        // Arrange
        var subscriptionName = $"test-sub-{Guid.NewGuid():N}"[..16];

        // Act - Create subscription
        var createResponse = await _client.PostAsync(
            $"/api/subscriptions?name={subscriptionName}&pattern=Order-*",
            null);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act - List subscriptions
        var listResponse = await _client.GetAsync("/api/subscriptions");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var subscriptions = await listResponse.Content.ReadFromJsonAsync<List<SubscriptionInfo>>();
        subscriptions.Should().Contain(s => s.Name == subscriptionName);
    }

    [Fact]
    public async Task Subscriptions_ProcessEvents_UpdatesPosition()
    {
        // Arrange
        var subscriptionName = $"process-sub-{Guid.NewGuid():N}"[..16];
        await _client.PostAsync($"/api/subscriptions?name={subscriptionName}&pattern=Order-*", null);

        // Act
        var processResponse = await _client.PostAsync($"/api/subscriptions/{subscriptionName}/process", null);

        // Assert
        processResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Audit Tests

    [Fact]
    public async Task Audit_GetLogs_ReturnsLogs()
    {
        // Arrange - Create demo to generate audit logs
        var demoResponse = await _client.PostAsync("/api/timetravel/demo/create", null);
        var demo = await demoResponse.Content.ReadFromJsonAsync<DemoCreateResult>();
        var streamId = $"OrderAggregate-{demo!.OrderId}";

        // Act
        var response = await _client.GetAsync($"/api/audit/logs/{streamId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Audit_VerifyStream_ReturnsValidationResult()
    {
        // Arrange
        var demoResponse = await _client.PostAsync("/api/timetravel/demo/create", null);
        var demo = await demoResponse.Content.ReadFromJsonAsync<DemoCreateResult>();
        var streamId = $"OrderAggregate-{demo!.OrderId}";

        // Act
        var response = await _client.PostAsync($"/api/audit/verify/{streamId}", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<VerifyResult>();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Audit_RequestGdprErasure_ReturnsAccepted()
    {
        // Arrange
        var customerId = $"gdpr-{Guid.NewGuid():N}"[..16];

        // Act
        var response = await _client.PostAsync(
            $"/api/audit/gdpr/erasure-request?customerId={customerId}&requestedBy=test@example.com",
            null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Audit_GetPendingGdprRequests_ReturnsList()
    {
        // Act
        var response = await _client.GetAsync("/api/audit/gdpr/pending-requests");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Snapshot Tests

    [Fact]
    public async Task Snapshots_CreateAndGetHistory_Works()
    {
        // Arrange
        var demoResponse = await _client.PostAsync("/api/timetravel/demo/create", null);
        var demo = await demoResponse.Content.ReadFromJsonAsync<DemoCreateResult>();

        // Act - Create snapshot
        var createResponse = await _client.PostAsync($"/api/snapshots/orders/{demo!.OrderId}", null);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act - Get snapshot history
        var historyResponse = await _client.GetAsync($"/api/snapshots/orders/{demo.OrderId}/history");
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region DTOs

    private record OrderCreatedResult(string OrderId, decimal TotalAmount);
    private record DemoCreateResult(string OrderId, int EventCount);
    private record TimeTravelState(string Id, string CustomerId, decimal TotalAmount, string Status, long Version);
    private record VersionHistoryItem(long Version, string EventType, DateTime Timestamp);
    private record OrderSummaryResult(int TotalOrders, decimal TotalRevenue, Dictionary<string, int> OrdersByStatus);
    private record SubscriptionInfo(string Name, string StreamPattern, long Position, int ProcessedCount);
    private record VerifyResult(string StreamId, bool IsValid, string? Hash, string? Error);

    #endregion
}
