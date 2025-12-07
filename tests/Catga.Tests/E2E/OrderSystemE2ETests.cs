using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// End-to-end tests for OrderSystem.Api example.
/// Tests the complete API workflow.
/// </summary>
public class OrderSystemE2ETests : IClassFixture<WebApplicationFactory<OrderSystem.Api.Program>>
{
    private readonly HttpClient _client;

    public OrderSystemE2ETests(WebApplicationFactory<OrderSystem.Api.Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthCheck_ReturnsHealthy()
    {
        // Act
        var response = await _client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("Healthy");
    }

    [Fact]
    public async Task CreateOrder_ReturnsSuccess()
    {
        // Arrange
        var order = new
        {
            customerId = "test-customer",
            items = new[]
            {
                new { productId = "P1", productName = "Test Product", quantity = 1, unitPrice = 99.99m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", order);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TimeTravel_CreateDemoOrder_ReturnsOrderId()
    {
        // Act
        var response = await _client.PostAsync("/api/timetravel/demo/create", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<DemoCreateResult>();
        result!.OrderId.Should().NotBeNullOrEmpty();
        result.EventCount.Should().Be(7);
    }

    [Fact]
    public async Task TimeTravel_GetVersionHistory()
    {
        // Arrange - create demo order first
        var createResponse = await _client.PostAsync("/api/timetravel/demo/create", null);
        var createResult = await createResponse.Content.ReadFromJsonAsync<DemoCreateResult>();

        // Act
        var response = await _client.GetAsync($"/api/timetravel/orders/{createResult!.OrderId}/history");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TimeTravel_GetStateAtVersion()
    {
        // Arrange
        var createResponse = await _client.PostAsync("/api/timetravel/demo/create", null);
        var createResult = await createResponse.Content.ReadFromJsonAsync<DemoCreateResult>();

        // Act - get state at version 3
        var response = await _client.GetAsync($"/api/timetravel/orders/{createResult!.OrderId}/version/3");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Projections_GetOrderSummary()
    {
        // Act
        var response = await _client.GetAsync("/api/projections/order-summary");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Projections_RebuildOrderSummary()
    {
        // Act
        var response = await _client.PostAsync("/api/projections/order-summary/rebuild", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Subscriptions_ListSubscriptions()
    {
        // Act
        var response = await _client.GetAsync("/api/subscriptions");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Audit_VerifyStream()
    {
        // Arrange - create demo order first
        var createResponse = await _client.PostAsync("/api/timetravel/demo/create", null);
        var createResult = await createResponse.Content.ReadFromJsonAsync<DemoCreateResult>();
        var streamId = $"OrderAggregate-{createResult!.OrderId}";

        // Act
        var response = await _client.PostAsync($"/api/audit/verify/{streamId}", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Audit_GetPendingGdprRequests()
    {
        // Act
        var response = await _client.GetAsync("/api/audit/gdpr/pending-requests");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Snapshots_GetHistory()
    {
        // Arrange - create demo order
        var createResponse = await _client.PostAsync("/api/timetravel/demo/create", null);
        var createResult = await createResponse.Content.ReadFromJsonAsync<DemoCreateResult>();

        // Act - get snapshot history (may be empty initially)
        var historyResponse = await _client.GetAsync($"/api/snapshots/orders/{createResult!.OrderId}/history");

        // Assert
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CompleteWorkflow_CreateOrderAndVerify()
    {
        // 1. Create order via flow
        var order = new
        {
            customerId = "workflow-customer",
            items = new[]
            {
                new { productId = "P1", productName = "Laptop", quantity = 1, unitPrice = 999.99m },
                new { productId = "P2", productName = "Mouse", quantity = 2, unitPrice = 29.99m }
            }
        };

        var createResponse = await _client.PostAsJsonAsync("/api/orders/flow", order);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Get projections
        var projResponse = await _client.GetAsync("/api/projections/order-summary");
        projResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Check subscriptions
        var subsResponse = await _client.GetAsync("/api/subscriptions");
        subsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private record DemoCreateResult(string OrderId, int EventCount, string Message);

    [Fact]
    public async Task Subscriptions_CreateAndProcess()
    {
        // Arrange - create demo order first
        await _client.PostAsync("/api/timetravel/demo/create", null);

        // Act - create subscription
        var createResponse = await _client.PostAsync("/api/subscriptions?name=e2e-test-sub&pattern=Order*", null);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Process events
        var processResponse = await _client.PostAsync("/api/subscriptions/e2e-test-sub/process", null);
        processResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await processResponse.Content.ReadFromJsonAsync<SubscriptionProcessResult>();
        result!.ProcessedCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Snapshots_CreateAndRetrieve()
    {
        // Arrange - create demo order
        var createResponse = await _client.PostAsync("/api/timetravel/demo/create", null);
        var createResult = await createResponse.Content.ReadFromJsonAsync<DemoCreateResult>();

        // Act - create snapshot
        var snapshotResponse = await _client.PostAsync($"/api/snapshots/orders/{createResult!.OrderId}", null);
        snapshotResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Get history
        var historyResponse = await _client.GetAsync($"/api/snapshots/orders/{createResult.OrderId}/history");
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TimeTravel_CompareVersions()
    {
        // Arrange
        var createResponse = await _client.PostAsync("/api/timetravel/demo/create", null);
        var createResult = await createResponse.Content.ReadFromJsonAsync<DemoCreateResult>();

        // Act - compare version 1 and 5
        var response = await _client.GetAsync($"/api/timetravel/orders/{createResult!.OrderId}/compare/1/5");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TimeTravel_GetTimeline()
    {
        // Arrange
        var createResponse = await _client.PostAsync("/api/timetravel/demo/create", null);
        var createResult = await createResponse.Content.ReadFromJsonAsync<DemoCreateResult>();

        // Act
        var response = await _client.GetAsync($"/api/timetravel/orders/{createResult!.OrderId}/timeline");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Projections_GetCustomerStats()
    {
        // Act
        var response = await _client.GetAsync("/api/projections/customer-stats");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Audit_RequestGdprErasure()
    {
        // Act
        var response = await _client.PostAsync("/api/audit/gdpr/erasure-request?customerId=test-customer&requestedBy=admin", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Audit_VerifyStream_ReturnsValid()
    {
        // Arrange
        var createResponse = await _client.PostAsync("/api/timetravel/demo/create", null);
        var createResult = await createResponse.Content.ReadFromJsonAsync<DemoCreateResult>();
        var streamId = $"OrderAggregate-{createResult!.OrderId}";

        // Act
        var response = await _client.PostAsync($"/api/audit/verify/{streamId}", null);
        var result = await response.Content.ReadFromJsonAsync<VerifyResult>();

        // Assert
        result!.IsValid.Should().BeTrue();
        result.Hash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Snapshots_LoadAtVersion()
    {
        // Arrange - create demo order and snapshot
        var createResponse = await _client.PostAsync("/api/timetravel/demo/create", null);
        var createResult = await createResponse.Content.ReadFromJsonAsync<DemoCreateResult>();
        await _client.PostAsync($"/api/snapshots/orders/{createResult!.OrderId}", null);

        // Act - try to load at version 6
        var response = await _client.GetAsync($"/api/snapshots/orders/{createResult.OrderId}/version/6");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task FullEventSourcingWorkflow()
    {
        // 1. Create demo order with events
        var createResponse = await _client.PostAsync("/api/timetravel/demo/create", null);
        var createResult = await createResponse.Content.ReadFromJsonAsync<DemoCreateResult>();
        createResult!.EventCount.Should().Be(7);

        // 2. Rebuild projections
        var rebuildResponse = await _client.PostAsync("/api/projections/order-summary/rebuild", null);
        rebuildResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Verify stream integrity
        var streamId = $"OrderAggregate-{createResult.OrderId}";
        var verifyResponse = await _client.PostAsync($"/api/audit/verify/{streamId}", null);
        var verifyResult = await verifyResponse.Content.ReadFromJsonAsync<VerifyResult>();
        verifyResult!.IsValid.Should().BeTrue();

        // 4. Create snapshot
        var snapshotResponse = await _client.PostAsync($"/api/snapshots/orders/{createResult.OrderId}", null);
        snapshotResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Time travel to version 3
        var v3Response = await _client.GetAsync($"/api/timetravel/orders/{createResult.OrderId}/version/3");
        v3Response.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Get timeline
        var timelineResponse = await _client.GetAsync($"/api/timetravel/orders/{createResult.OrderId}/timeline");
        timelineResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private record SubscriptionProcessResult(string Name, int ProcessedCount);
    private record VerifyResult(string StreamId, bool IsValid, string Hash, string? Error);
}
