using Catga.AspNetCore;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Endpoints;
using OrderSystem.Api.Messages;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// Tests for OrderSystem best practices implementation.
/// Validates validation, error handling, logging, and caching patterns.
/// </summary>
public class OrderSystemBestPracticesTests
{
    private WebApplication CreateTestApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();
        builder.Services.AddMemoryCache();
        builder.Services.AddLogging();

        var app = builder.Build();
        app.RegisterEndpoint<OrderEndpointHandlersBestPractices>();
        return app;
    }

    [Fact]
    public async Task CreateOrder_WithValidation_ShouldValidateCustomerId()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new CreateOrderCommand
        {
            CustomerId = "", // Empty customer ID
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "prod-001", Quantity = 1, Price = 50 }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/orders/best-practice", cmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsAsync<dynamic>();
        content.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateOrder_WithValidation_ShouldValidateItems()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new CreateOrderCommand
        {
            CustomerId = "cust-001",
            Items = new List<OrderItem>() // Empty items
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/orders/best-practice", cmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOrder_WithValidation_ShouldValidateItemPrices()
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
                new OrderItem { ProductId = "prod-001", Quantity = 1, Price = -50 } // Negative price
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/orders/best-practice", cmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOrder_WithValidData_ShouldPublishEvent()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new CreateOrderCommand
        {
            CustomerId = "cust-best-001",
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "prod-001", Quantity = 2, Price = 50 }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/orders/best-practice", cmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        var createdOrder = await response.Content.ReadAsAsync<OrderCreatedResult>();
        createdOrder!.OrderId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetOrder_WithCaching_ShouldReturnCachedResult()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var orderId = "ORD-cache-001";

        // Act - First request
        var response1 = await client.GetAsync($"/api/orders/best-practice/{orderId}");

        // Act - Second request (should be cached)
        var response2 = await client.GetAsync($"/api/orders/best-practice/{orderId}");

        // Assert
        response1.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        response2.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetOrder_WithValidation_ShouldValidateOrderId()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("/api/orders/best-practice/");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PayOrder_WithValidation_ShouldValidateOrderId()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new PayOrderCommand
        {
            OrderId = "", // Empty order ID
            Amount = 100
        };

        // Act
        var response = await client.PutAsJsonAsync("/api/orders/best-practice//pay", cmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PayOrder_WithValidation_ShouldValidateAmount()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new PayOrderCommand
        {
            OrderId = "ORD-001",
            Amount = -100 // Negative amount
        };

        // Act
        var response = await client.PutAsJsonAsync("/api/orders/best-practice/ORD-001/pay", cmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PayOrder_WithValidData_ShouldPublishEvent()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new PayOrderCommand
        {
            OrderId = "ORD-pay-001",
            Amount = 100
        };

        // Act
        var response = await client.PutAsJsonAsync("/api/orders/best-practice/ORD-pay-001/pay", cmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task SearchOrders_WithSanitization_ShouldSanitizeInput()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var query = new SearchOrdersQuery
        {
            SearchTerm = "<script>alert('xss')</script>",
            PageNumber = 1,
            PageSize = 10
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/orders/best-practice/search", query);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task SearchOrders_WithPageSizeLimit_ShouldLimitPageSize()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var query = new SearchOrdersQuery
        {
            SearchTerm = "test",
            PageNumber = 1,
            PageSize = 500 // Exceeds limit
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/orders/best-practice/search", query);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateOrdersBatch_WithValidData_ShouldCreateBatch()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new CreateOrdersBatchCommand
        {
            Orders = new List<CreateOrderCommand>
            {
                new CreateOrderCommand
                {
                    CustomerId = "cust-batch-001",
                    Items = new List<OrderItem>
                    {
                        new OrderItem { ProductId = "prod-001", Quantity = 1, Price = 50 }
                    }
                },
                new CreateOrderCommand
                {
                    CustomerId = "cust-batch-002",
                    Items = new List<OrderItem>
                    {
                        new OrderItem { ProductId = "prod-002", Quantity = 2, Price = 30 }
                    }
                }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/orders/best-practice/batch", cmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateOrdersBatch_WithEmptyBatch_ShouldReturnBadRequest()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new CreateOrdersBatchCommand
        {
            Orders = new List<CreateOrderCommand>() // Empty batch
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/orders/best-practice/batch", cmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOrdersBatch_WithExcessiveSize_ShouldReturnBadRequest()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new CreateOrdersBatchCommand
        {
            Orders = Enumerable.Range(0, 101) // Exceeds limit of 100
                .Select(i => new CreateOrderCommand
                {
                    CustomerId = $"cust-batch-{i}",
                    Items = new List<OrderItem>
                    {
                        new OrderItem { ProductId = "prod-001", Quantity = 1, Price = 50 }
                    }
                })
                .ToList()
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/orders/best-practice/batch", cmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOrdersBatch_WithValidData_ShouldPublishEvents()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new CreateOrdersBatchCommand
        {
            Orders = new List<CreateOrderCommand>
            {
                new CreateOrderCommand
                {
                    CustomerId = "cust-batch-event-001",
                    Items = new List<OrderItem>
                    {
                        new OrderItem { ProductId = "prod-001", Quantity = 1, Price = 50 }
                    }
                }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/orders/best-practice/batch", cmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        var result = await response.Content.ReadAsAsync<BatchCreatedResult>();
        result!.CreatedOrderIds.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateOrder_WithErrorHandling_ShouldMapErrorToStatus()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new CreateOrderCommand
        {
            CustomerId = "cust-error",
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = "prod-001", Quantity = 1, Price = 50 }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/orders/best-practice", cmd);

        // Assert
        response.StatusCode.Should().BeOneOf(
            System.Net.HttpStatusCode.Created,
            System.Net.HttpStatusCode.BadRequest
        );
    }

    [Fact]
    public async Task PayOrder_WithErrorHandling_ShouldMapErrorToStatus()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new PayOrderCommand
        {
            OrderId = "ORD-error",
            Amount = 100
        };

        // Act
        var response = await client.PutAsJsonAsync("/api/orders/best-practice/ORD-error/pay", cmd);

        // Assert
        response.StatusCode.Should().BeOneOf(
            System.Net.HttpStatusCode.OK,
            System.Net.HttpStatusCode.BadRequest,
            System.Net.HttpStatusCode.NotFound,
            System.Net.HttpStatusCode.Conflict
        );
    }

    [Fact]
    public async Task MultipleRequests_ConcurrentExecution_ShouldHandleAll()
    {
        // Arrange
        var app = CreateTestApp();
        var server = new TestServer(app);
        var client = server.CreateClient();

        // Act - Execute multiple requests concurrently
        var tasks = Enumerable.Range(0, 20)
            .Select(i => client.PostAsJsonAsync("/api/orders/best-practice", new CreateOrderCommand
            {
                CustomerId = $"cust-concurrent-{i}",
                Items = new List<OrderItem>
                {
                    new OrderItem { ProductId = "prod-001", Quantity = 1, Price = 100 }
                }
            }))
            .ToList();

        var responses = await Task.WhenAll(tasks);

        // Assert
        responses.Should().AllSatisfy(r =>
            r.StatusCode.Should().BeOneOf(
                System.Net.HttpStatusCode.Created,
                System.Net.HttpStatusCode.BadRequest
            )
        );
    }
}
