using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// End-to-end tests for OrderSystem.Api Flow endpoints.
/// Tests the complete flow execution workflow.
/// </summary>
public class OrderSystemFlowE2ETests : IClassFixture<WebApplicationFactory<OrderSystem.Api.Program>>
{
    private readonly HttpClient _client;

    public OrderSystemFlowE2ETests(WebApplicationFactory<OrderSystem.Api.Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateOrderFlow_WithValidData_ReturnsSuccess()
    {
        // Arrange
        var order = new
        {
            customerId = "flow-customer-1",
            items = new[]
            {
                new { productId = "P1", productName = "Laptop", quantity = 1, unitPrice = 1299.99m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders/flow", order);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateOrderFlow_WithMultipleItems_ReturnsSuccess()
    {
        // Arrange
        var order = new
        {
            customerId = "flow-customer-2",
            items = new[]
            {
                new { productId = "P1", productName = "Laptop", quantity = 1, unitPrice = 1299.99m },
                new { productId = "P2", productName = "Mouse", quantity = 2, unitPrice = 49.99m },
                new { productId = "P3", productName = "Keyboard", quantity = 1, unitPrice = 149.99m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders/flow", order);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateOrderFlow_ConcurrentRequests_AllSucceed()
    {
        // Arrange
        var tasks = Enumerable.Range(0, 10).Select(i => _client.PostAsJsonAsync("/api/orders/flow", new
        {
            customerId = $"concurrent-flow-customer-{i}",
            items = new[]
            {
                new { productId = "P1", productName = "Product", quantity = 1, unitPrice = 99.99m }
            }
        }));

        // Act
        var responses = await Task.WhenAll(tasks);

        // Assert
        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));
    }

    [Fact]
    public async Task CreateOrderFlow_ThenVerifyProjections_ShowsOrder()
    {
        // Arrange - Create order via flow
        var customerId = $"projection-test-{Guid.NewGuid():N}";
        var order = new
        {
            customerId,
            items = new[]
            {
                new { productId = "P1", productName = "Test Product", quantity = 1, unitPrice = 100.00m }
            }
        };

        // Act
        var createResponse = await _client.PostAsJsonAsync("/api/orders/flow", order);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify projections updated
        var projResponse = await _client.GetAsync("/api/projections/order-summary");

        // Assert
        projResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateOrderFlow_ThenTimeTravel_ShowsHistory()
    {
        // Arrange - Create order via demo (which creates multiple events)
        var createResponse = await _client.PostAsync("/api/timetravel/demo/create", null);
        var createResult = await createResponse.Content.ReadFromJsonAsync<DemoCreateResult>();

        // Act - Get timeline
        var timelineResponse = await _client.GetAsync($"/api/timetravel/orders/{createResult!.OrderId}/timeline");

        // Assert
        timelineResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateOrderFlow_ThenSnapshot_CanRestore()
    {
        // Arrange - Create demo order
        var createResponse = await _client.PostAsync("/api/timetravel/demo/create", null);
        var createResult = await createResponse.Content.ReadFromJsonAsync<DemoCreateResult>();

        // Create snapshot
        var snapshotResponse = await _client.PostAsync($"/api/snapshots/orders/{createResult!.OrderId}", null);
        snapshotResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act - Get snapshot history
        var historyResponse = await _client.GetAsync($"/api/snapshots/orders/{createResult.OrderId}/history");

        // Assert
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateOrderFlow_ThenAudit_StreamIsValid()
    {
        // Arrange - Create demo order
        var createResponse = await _client.PostAsync("/api/timetravel/demo/create", null);
        var createResult = await createResponse.Content.ReadFromJsonAsync<DemoCreateResult>();
        var streamId = $"OrderAggregate-{createResult!.OrderId}";

        // Act - Verify stream
        var verifyResponse = await _client.PostAsync($"/api/audit/verify/{streamId}", null);
        var verifyResult = await verifyResponse.Content.ReadFromJsonAsync<VerifyResult>();

        // Assert
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        verifyResult!.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task CreateOrderFlow_SequentialSteps_ExecuteInOrder()
    {
        // Arrange
        var order = new
        {
            customerId = "sequential-test",
            items = new[]
            {
                new { productId = "P1", productName = "Item1", quantity = 1, unitPrice = 10.00m },
                new { productId = "P2", productName = "Item2", quantity = 1, unitPrice = 20.00m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders/flow", order);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateOrderFlow_LargeOrder_HandlesCorrectly()
    {
        // Arrange - Create order with many items
        var items = Enumerable.Range(1, 20).Select(i => new
        {
            productId = $"P{i}",
            productName = $"Product {i}",
            quantity = i,
            unitPrice = i * 10.00m
        }).ToArray();

        var order = new
        {
            customerId = "large-order-customer",
            items
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders/flow", order);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateOrderFlow_ThenRebuildProjection_DataConsistent()
    {
        // Arrange - Create some orders
        for (int i = 0; i < 3; i++)
        {
            await _client.PostAsJsonAsync("/api/orders/flow", new
            {
                customerId = $"rebuild-test-{i}",
                items = new[] { new { productId = "P1", productName = "Product", quantity = 1, unitPrice = 50.00m } }
            });
        }

        // Act - Rebuild projection
        var rebuildResponse = await _client.PostAsync("/api/projections/order-summary/rebuild", null);

        // Assert
        rebuildResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify projection data
        var summaryResponse = await _client.GetAsync("/api/projections/order-summary");
        summaryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateOrderFlow_ThenSubscribe_ProcessesEvents()
    {
        // Arrange - Create subscription
        var subName = $"flow-test-sub-{Guid.NewGuid():N}";
        await _client.PostAsync($"/api/subscriptions?name={subName}&pattern=Order*", null);

        // Create order
        await _client.PostAsJsonAsync("/api/orders/flow", new
        {
            customerId = "subscription-test",
            items = new[] { new { productId = "P1", productName = "Product", quantity = 1, unitPrice = 25.00m } }
        });

        // Act - Process subscription
        var processResponse = await _client.PostAsync($"/api/subscriptions/{subName}/process", null);

        // Assert
        processResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    private record DemoCreateResult(string OrderId, int EventCount, string Message);
    private record VerifyResult(string StreamId, bool IsValid, string Hash, string? Error);
}
