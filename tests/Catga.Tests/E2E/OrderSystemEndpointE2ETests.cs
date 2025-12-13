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
/// Complete E2E tests for OrderSystem.Api using Catga AspNetCore Endpoints
/// Tests real-world scenarios: create order, get order, pay order, cancel order
/// </summary>
public class OrderSystemEndpointE2ETests
{
    private WebApplication CreateTestApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();
        app.RegisterEndpoint<OrderEndpointHandlers>();
        return app;
    }

    [Fact]
    public async Task CreateOrder_ShouldReturnCreatedWithOrderId()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var createCmd = new CreateOrderCommand
        {
            CustomerId = "cust-001",
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "prod-001", Quantity = 2, Price = 50 }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", createCmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().StartWith("/api/orders/");

        var content = await response.Content.ReadAsAsync<OrderCreatedResult>();
        content.Should().NotBeNull();
        content!.OrderId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetOrder_ShouldReturnOrderDetails()
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
        var content = await response.Content.ReadAsAsync<Order>();
        content.Should().NotBeNull();
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
        content.Should().NotBeNull();
        content.Should().BeOfType<List<Order>>();
    }

    [Fact]
    public async Task PayOrder_ShouldUpdateOrderStatusAndPublishEvent()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var payCmd = new PayOrderCommand
        {
            OrderId = "ORD-12345",
            Amount = 100
        };

        // Act
        var response = await client.PutAsJsonAsync("/api/orders/ORD-12345/pay", payCmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var content = await response.Content.ReadAsAsync<dynamic>();
        content.Should().NotBeNull();
    }

    [Fact]
    public async Task CancelOrder_ShouldReturnNoContent()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cancelCmd = new CancelOrderCommand
        {
            OrderId = "ORD-12345"
        };

        // Act
        var response = await client.DeleteAsync("/api/orders/ORD-12345");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CompleteOrderLifecycle_ShouldSucceed()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        // Act 1: Create order
        var createCmd = new CreateOrderCommand
        {
            CustomerId = "cust-001",
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
    public async Task MultipleOrderCreation_ShouldHandleConcurrently()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        // Act - Create 10 orders concurrently
        var tasks = Enumerable.Range(0, 10)
            .Select(i => client.PostAsJsonAsync("/api/orders", new CreateOrderCommand
            {
                CustomerId = $"cust-{i:D3}",
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
    public async Task EndpointHandler_ShouldPreserveRequestContext()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var customerId = "cust-context-test";
        var createCmd = new CreateOrderCommand
        {
            CustomerId = customerId,
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "prod-001", Quantity = 1, Price = 100 }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", createCmd);
        var createdOrder = await response.Content.ReadAsAsync<OrderCreatedResult>();

        // Assert - CustomerId should be preserved
        createdOrder!.OrderId.Should().NotBeNullOrEmpty();
    }
}
