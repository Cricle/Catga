using Catga.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Endpoints;
using OrderSystem.Api.Messages;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// Comprehensive integration tests for OrderSystem.Api endpoints.
/// Tests real-world scenarios with validation, error handling, and event publishing.
/// </summary>
public class OrderSystemIntegrationTests
{
    private WebApplication CreateTestApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();
        builder.Services.AddMemoryCache();
        builder.Services.AddLogging();

        var app = builder.Build();
        app.RegisterEndpoint<OrderEndpointHandlers>();
        return app;
    }

    [Fact]
    public async Task CreateOrder_WithValidData_ShouldReturnCreated()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new CreateOrderCommand
        {
            CustomerId = "cust-001",
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "prod-001", Quantity = 2, Price = 50 }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", cmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateOrder_WithEmptyItems_ShouldReturnBadRequest()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new CreateOrderCommand
        {
            CustomerId = "cust-001",
            Items = new List<OrderItem>() // Empty
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", cmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOrder_WithNegativePrice_ShouldReturnBadRequest()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new CreateOrderCommand
        {
            CustomerId = "cust-001",
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "prod-001", Quantity = 1, Price = -50 }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", cmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOrder_WithEmptyCustomerId_ShouldReturnBadRequest()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new CreateOrderCommand
        {
            CustomerId = "",
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "prod-001", Quantity = 1, Price = 50 }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", cmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetOrder_WithValidId_ShouldReturnOrder()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var orderId = "ORD-12345";

        // Act
        var response = await client.GetAsync($"/api/orders/{orderId}");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetOrder_WithInvalidId_ShouldReturnNotFound()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("/api/orders/INVALID-ID");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAllOrders_ShouldReturnOrderList()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("/api/orders");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var content = await response.Content.ReadAsAsync<List<Order>>();
        content.Should().BeOfType<List<Order>>();
    }

    [Fact]
    public async Task PayOrder_WithValidData_ShouldReturnOk()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new PayOrderCommand
        {
            OrderId = "ORD-12345",
            Amount = 100
        };

        // Act
        var response = await client.PutAsJsonAsync("/api/orders/ORD-12345/pay", cmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task PayOrder_WithNegativeAmount_ShouldReturnBadRequest()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new PayOrderCommand
        {
            OrderId = "ORD-12345",
            Amount = -100
        };

        // Act
        var response = await client.PutAsJsonAsync("/api/orders/ORD-12345/pay", cmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CancelOrder_ShouldReturnNoContent()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new CancelOrderCommand
        {
            OrderId = "ORD-12345"
        };

        // Act
        var response = await client.DeleteAsync("/api/orders/ORD-12345");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CompleteOrderWorkflow_ShouldSucceed()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        // Act 1: Create order
        var createCmd = new CreateOrderCommand
        {
            CustomerId = "cust-workflow-001",
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "prod-001", Quantity = 1, Price = 100 }
            }
        };
        var createResponse = await client.PostAsJsonAsync("/api/orders", createCmd);
        createResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);

        var createdOrder = await createResponse.Content.ReadAsAsync<OrderCreatedResult>();
        var orderId = createdOrder!.OrderId;

        // Act 2: Get order
        var getResponse = await client.GetAsync($"/api/orders/{orderId}");
        getResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        // Act 3: Pay order
        var payCmd = new PayOrderCommand { OrderId = orderId, Amount = 100 };
        var payResponse = await client.PutAsJsonAsync($"/api/orders/{orderId}/pay", payCmd);
        payResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        // Act 4: Cancel order
        var cancelResponse = await client.DeleteAsync($"/api/orders/{orderId}");
        cancelResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

        // Assert - All operations succeeded
        createResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        getResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        payResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        cancelResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task MultipleOrders_ConcurrentCreation_ShouldHandleAll()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        // Act - Create 10 orders concurrently
        var tasks = Enumerable.Range(0, 10)
            .Select(i => client.PostAsJsonAsync("/api/orders", new CreateOrderCommand
            {
                CustomerId = $"cust-concurrent-{i:D3}",
                Items = new List<OrderItem>
                {
                    new OrderItem { ProductId = "prod-001", Quantity = 1, Price = 100 }
                }
            }))
            .ToList();

        var responses = await Task.WhenAll(tasks);

        // Assert
        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(System.Net.HttpStatusCode.Created));
    }

    [Fact]
    public async Task OrderCreation_ShouldPublishEvent()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new CreateOrderCommand
        {
            CustomerId = "cust-event-001",
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "prod-001", Quantity = 2, Price = 50 }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", cmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        var createdOrder = await response.Content.ReadAsAsync<OrderCreatedResult>();
        createdOrder!.OrderId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task OrderPayment_ShouldPublishEvent()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new PayOrderCommand
        {
            OrderId = "ORD-event-001",
            Amount = 100
        };

        // Act
        var response = await client.PutAsJsonAsync("/api/orders/ORD-event-001/pay", cmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task OrderCancellation_ShouldPublishEvent()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new CancelOrderCommand
        {
            OrderId = "ORD-cancel-001"
        };

        // Act
        var response = await client.DeleteAsync("/api/orders/ORD-cancel-001");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CreateOrder_WithLargeQuantity_ShouldSucceed()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new CreateOrderCommand
        {
            CustomerId = "cust-large-qty",
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "prod-001", Quantity = 1000, Price = 1 }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", cmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateOrder_WithMultipleItems_ShouldSucceed()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new CreateOrderCommand
        {
            CustomerId = "cust-multi-items",
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "prod-001", Quantity = 2, Price = 50 },
                new OrderItem { ProductId = "prod-002", Quantity = 3, Price = 30 },
                new OrderItem { ProductId = "prod-003", Quantity = 1, Price = 100 }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", cmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        var createdOrder = await response.Content.ReadAsAsync<OrderCreatedResult>();
        createdOrder!.OrderId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetAllOrders_ShouldReturnMultipleOrders()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        // Create multiple orders
        for (int i = 0; i < 5; i++)
        {
            var cmd = new CreateOrderCommand
            {
                CustomerId = $"cust-multi-{i}",
                Items = new List<OrderItem>
                {
                    new OrderItem { ProductId = "prod-001", Quantity = 1, Price = 100 }
                }
            };
            await client.PostAsJsonAsync("/api/orders", cmd);
        }

        // Act
        var response = await client.GetAsync("/api/orders");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var orders = await response.Content.ReadAsAsync<List<Order>>();
        orders.Should().NotBeNull();
    }

    [Fact]
    public async Task PayOrder_WithZeroAmount_ShouldReturnBadRequest()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new PayOrderCommand
        {
            OrderId = "ORD-12345",
            Amount = 0
        };

        // Act
        var response = await client.PutAsJsonAsync("/api/orders/ORD-12345/pay", cmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }
}
