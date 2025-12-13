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
/// E2E tests for error handling in Catga AspNetCore Endpoints
/// Tests validation errors, command failures, exception handling
/// </summary>
public class AspNetCoreEndpointErrorHandlingTests
{
    [Fact]
    public async Task EndpointHandler_ShouldReturnBadRequest_WhenCommandValidationFails()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();
        app.RegisterEndpoint<TestErrorHandlingHandlers>();

        var server = new TestServer(app);
        var client = server.CreateClient();

        var invalidCmd = new TestValidatedCommand { Value = "" }; // Invalid: empty value

        // Act
        var response = await client.PostAsJsonAsync("/api/validate", invalidCmd);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EndpointHandler_ShouldIncludeErrorMessage_InBadRequestResponse()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();
        app.RegisterEndpoint<TestErrorHandlingHandlers>();

        var server = new TestServer(app);
        var client = server.CreateClient();

        var invalidCmd = new TestValidatedCommand { Value = "" };

        // Act
        var response = await client.PostAsJsonAsync("/api/validate", invalidCmd);
        var content = await response.Content.ReadAsAsync<dynamic>();

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        content.Should().NotBeNull();
        content.error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task EndpointHandler_ShouldReturnNotFound_WhenResourceDoesNotExist()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();
        app.RegisterEndpoint<TestErrorHandlingHandlers>();

        var server = new TestServer(app);
        var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("/api/resource/non-existent-id");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EndpointHandler_ShouldReturnConflict_WhenResourceAlreadyExists()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();
        app.RegisterEndpoint<TestErrorHandlingHandlers>();

        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new TestDuplicateCommand { Id = "duplicate-id" };

        // Act - First request should succeed
        var response1 = await client.PostAsJsonAsync("/api/duplicate", cmd);
        response1.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        // Act - Second request with same ID should fail
        var response2 = await client.PostAsJsonAsync("/api/duplicate", cmd);

        // Assert
        response2.StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task EndpointHandler_ShouldHandleNullRequestBody()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();
        app.RegisterEndpoint<TestErrorHandlingHandlers>();

        var server = new TestServer(app);
        var client = server.CreateClient();

        // Act - Send null body
        var request = new System.Net.Http.HttpRequestMessage(
            System.Net.Http.HttpMethod.Post,
            "/api/validate")
        {
            Content = new System.Net.Http.StringContent("null", System.Text.Encoding.UTF8, "application/json")
        };
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EndpointHandler_ShouldHandleMissingRequiredParameter()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();
        app.RegisterEndpoint<TestErrorHandlingHandlers>();

        var server = new TestServer(app);
        var client = server.CreateClient();

        // Act - Missing required route parameter
        var response = await client.GetAsync("/api/resource/");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EndpointHandler_ShouldReturnInternalServerError_OnUnexpectedException()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();
        app.RegisterEndpoint<TestErrorHandlingHandlers>();

        var server = new TestServer(app);
        var client = server.CreateClient();

        // Act - Trigger exception
        var response = await client.PostAsJsonAsync("/api/error", new TestErrorCommand { });

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task EndpointHandler_ShouldPreserveErrorContext_AcrossRequests()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();
        app.RegisterEndpoint<TestErrorHandlingHandlers>();

        var server = new TestServer(app);
        var client = server.CreateClient();

        // Act - First request with error
        var response1 = await client.PostAsJsonAsync("/api/validate", new TestValidatedCommand { Value = "" });

        // Act - Second request should work normally
        var response2 = await client.PostAsJsonAsync("/api/validate", new TestValidatedCommand { Value = "valid" });

        // Assert
        response1.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        response2.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }
}

/// <summary>
/// Test handler for error handling scenarios
/// </summary>
public partial class TestErrorHandlingHandlers
{
    [CatgaEndpoint(HttpMethod.Post, "/api/validate")]
    public partial async Task<IResult> ValidateCommand(TestValidatedCommand cmd, ICatgaMediator mediator);

    [CatgaEndpoint(HttpMethod.Get, "/api/resource/{id}")]
    public partial async Task<IResult> GetResource(TestGetResourceQuery query, ICatgaMediator mediator);

    [CatgaEndpoint(HttpMethod.Post, "/api/duplicate")]
    public partial async Task<IResult> CreateDuplicate(TestDuplicateCommand cmd, ICatgaMediator mediator);

    [CatgaEndpoint(HttpMethod.Post, "/api/error")]
    public partial async Task<IResult> TriggerError(TestErrorCommand cmd, ICatgaMediator mediator);
}

public partial class TestErrorHandlingHandlers
{
    public partial async Task<IResult> ValidateCommand(TestValidatedCommand cmd, ICatgaMediator mediator)
    {
        if (string.IsNullOrEmpty(cmd.Value))
            return Results.BadRequest(new { error = "Value is required" });

        var result = await mediator.SendAsync<TestValidatedCommand, TestValidationResult>(cmd);
        return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
    }

    public partial async Task<IResult> GetResource(TestGetResourceQuery query, ICatgaMediator mediator)
    {
        if (query.Id == "non-existent-id")
            return Results.NotFound();

        var result = await mediator.SendAsync<TestGetResourceQuery, TestResourceDto>(query);
        return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound();
    }

    public partial async Task<IResult> CreateDuplicate(TestDuplicateCommand cmd, ICatgaMediator mediator)
    {
        var result = await mediator.SendAsync<TestDuplicateCommand, bool>(cmd);

        if (!result.IsSuccess && result.Error?.Contains("Conflict") == true)
            return Results.Conflict();

        return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
    }

    public partial async Task<IResult> TriggerError(TestErrorCommand cmd, ICatgaMediator mediator)
    {
        try
        {
            var result = await mediator.SendAsync<TestErrorCommand, bool>(cmd);
            return result.IsSuccess ? Results.Ok() : Results.BadRequest();
        }
        catch (Exception ex)
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

// Test types for error handling
public class TestValidatedCommand : IRequest<TestValidationResult>
{
    public string Value { get; set; }
}

public class TestValidationResult
{
    public bool IsValid { get; set; }
}

public class TestGetResourceQuery : IRequest<TestResourceDto>
{
    public string Id { get; set; }
}

public class TestResourceDto
{
    public string Id { get; set; }
    public string Name { get; set; }
}

public class TestDuplicateCommand : IRequest<bool>
{
    public string Id { get; set; }
}

public class TestErrorCommand : IRequest<bool>
{
}
