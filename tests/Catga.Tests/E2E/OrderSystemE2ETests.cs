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
}
