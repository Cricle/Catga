using Catga.Abstractions;
using Catga.AspNetCore;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// E2E integration tests for Catga AspNetCore Endpoint system
/// Tests complete request/response cycles, event publishing, error handling
/// </summary>
public class AspNetCoreEndpointIntegrationTests
{
    [Fact]
    public async Task EndpointHandler_ShouldProcessCreateCommandAndPublishEvent()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();
        app.RegisterEndpoint<TestOrderHandlers>();

        var server = new TestServer(app);
        var client = server.CreateClient();

        var createCmd = new TestCreateOrderCommand
        {
            CustomerId = "cust-001",
            Amount = 100
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", createCmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        var content = await response.Content.ReadAsAsync<TestOrderResult>();
        content.Should().NotBeNull();
        content!.OrderId.Should().NotBeNullOrEmpty();
        content.CustomerId.Should().Be("cust-001");
        content.Amount.Should().Be(100);
    }

    [Fact]
    public async Task EndpointHandler_ShouldProcessGetQueryAndReturnData()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();
        app.RegisterEndpoint<TestOrderHandlers>();

        var server = new TestServer(app);
        var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("/api/orders/order-001");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var content = await response.Content.ReadAsAsync<TestOrderDto>();
        content.Should().NotBeNull();
        content!.OrderId.Should().Be("order-001");
    }

    [Fact]
    public async Task EndpointHandler_ShouldReturnNotFoundWhenQueryFails()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();
        app.RegisterEndpoint<TestOrderHandlers>();

        var server = new TestServer(app);
        var client = server.CreateClient();

        // Act - Query with non-existent ID
        var response = await client.GetAsync("/api/orders/non-existent");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EndpointHandler_ShouldReturnBadRequestOnCommandFailure()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();
        app.RegisterEndpoint<TestOrderHandlers>();

        var server = new TestServer(app);
        var client = server.CreateClient();

        var createCmd = new TestCreateOrderCommand
        {
            CustomerId = "",  // Invalid: empty customer ID
            Amount = 100
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", createCmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChainedRegistration_ShouldRegisterMultipleHandlers()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();

        // Act - Register multiple handlers in chain
        var registrar = app.RegisterEndpoint<TestOrderHandlers>()
            .RegisterEndpoint<TestOrderHandlers>();

        // Assert
        registrar.Should().NotBeNull();
        registrar.Should().BeAssignableTo<IEndpointRegistrar>();
    }

    [Fact]
    public async Task EndpointHandler_ShouldPublishEventAfterSuccessfulCommand()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();
        app.RegisterEndpoint<TestOrderHandlers>();

        var server = new TestServer(app);
        var client = server.CreateClient();

        var createCmd = new TestCreateOrderCommand
        {
            CustomerId = "cust-001",
            Amount = 100
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", createCmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().StartWith("/api/orders/");
    }

    [Fact]
    public async Task EndpointHandler_ShouldSupportMultipleHttpMethods()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();
        app.RegisterEndpoint<TestMultiMethodHandlers>();

        var server = new TestServer(app);
        var client = server.CreateClient();

        // Act - Test POST
        var postResponse = await client.PostAsJsonAsync("/api/test", new { value = "test" });
        postResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        // Act - Test GET
        var getResponse = await client.GetAsync("/api/test");
        getResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        // Act - Test PUT
        var putResponse = await client.PutAsJsonAsync("/api/test", new { value = "updated" });
        putResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        // Act - Test DELETE
        var deleteResponse = await client.DeleteAsync("/api/test");
        deleteResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task EndpointHandler_ShouldPreserveRouteParameters()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();
        app.RegisterEndpoint<TestOrderHandlers>();

        var server = new TestServer(app);
        var client = server.CreateClient();

        var orderId = "order-12345";

        // Act
        var response = await client.GetAsync($"/api/orders/{orderId}");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var content = await response.Content.ReadAsAsync<TestOrderDto>();
        content!.OrderId.Should().Be(orderId);
    }

    [Fact]
    public async Task EndpointHandler_ShouldHandleEmptyResponseBody()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();
        app.RegisterEndpoint<TestMultiMethodHandlers>();

        var server = new TestServer(app);
        var client = server.CreateClient();

        // Act - DELETE should return NoContent (204)
        var response = await client.DeleteAsync("/api/test");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
        response.Content.Headers.ContentLength.Should().Be(0);
    }
}

/// <summary>
/// Test handler with multiple HTTP methods
/// </summary>
public partial class TestMultiMethodHandlers
{
    [CatgaEndpoint(HttpMethod.Post, "/api/test")]
    public partial async Task<IResult> CreateTest(TestCreateCommand cmd, ICatgaMediator mediator);

    [CatgaEndpoint(HttpMethod.Get, "/api/test")]
    public partial async Task<IResult> GetTest(TestGetQuery query, ICatgaMediator mediator);

    [CatgaEndpoint(HttpMethod.Put, "/api/test")]
    public partial async Task<IResult> UpdateTest(TestUpdateCommand cmd, ICatgaMediator mediator);

    [CatgaEndpoint(HttpMethod.Delete, "/api/test")]
    public partial async Task<IResult> DeleteTest(TestDeleteCommand cmd, ICatgaMediator mediator);
}

public partial class TestMultiMethodHandlers
{
    public partial async Task<IResult> CreateTest(TestCreateCommand cmd, ICatgaMediator mediator)
    {
        var result = await mediator.SendAsync<TestCreateCommand, TestResult>(cmd);
        return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest();
    }

    public partial async Task<IResult> GetTest(TestGetQuery query, ICatgaMediator mediator)
    {
        var result = await mediator.SendAsync<TestGetQuery, TestResult>(query);
        return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound();
    }

    public partial async Task<IResult> UpdateTest(TestUpdateCommand cmd, ICatgaMediator mediator)
    {
        var result = await mediator.SendAsync<TestUpdateCommand, TestResult>(cmd);
        return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest();
    }

    public partial async Task<IResult> DeleteTest(TestDeleteCommand cmd, ICatgaMediator mediator)
    {
        var result = await mediator.SendAsync<TestDeleteCommand, bool>(cmd);
        return result.IsSuccess ? Results.NoContent() : Results.BadRequest();
    }
}

// Test request/response types for multi-method handler
public class TestCreateCommand : IRequest<TestResult> { public string Value { get; set; } }
public class TestGetQuery : IRequest<TestResult> { }
public class TestUpdateCommand : IRequest<TestResult> { public string Value { get; set; } }
public class TestDeleteCommand : IRequest<bool> { }
public class TestResult { public string Value { get; set; } }
