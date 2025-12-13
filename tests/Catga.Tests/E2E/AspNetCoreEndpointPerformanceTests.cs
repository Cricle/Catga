using Catga.Abstractions;
using Catga.AspNetCore;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// Performance and concurrency tests for Catga AspNetCore Endpoints
/// Verifies zero-reflection performance, concurrent request handling
/// </summary>
public class AspNetCoreEndpointPerformanceTests
{
    [Fact]
    public async Task EndpointHandler_ShouldHandleMultipleConcurrentRequests()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();
        app.RegisterEndpoint<TestPerformanceHandlers>();

        var server = new TestServer(app);
        var client = server.CreateClient();

        var tasks = new List<Task<System.Net.Http.HttpResponseMessage>>();

        // Act - Send 100 concurrent requests
        for (int i = 0; i < 100; i++)
        {
            var cmd = new TestConcurrentCommand { Id = i.ToString(), Value = $"test-{i}" };
            tasks.Add(client.PostAsJsonAsync("/api/concurrent", cmd));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert
        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(System.Net.HttpStatusCode.OK));
    }

    [Fact]
    public async Task EndpointHandler_ShouldMaintainPerformanceUnderLoad()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();
        app.RegisterEndpoint<TestPerformanceHandlers>();

        var server = new TestServer(app);
        var client = server.CreateClient();

        var stopwatch = Stopwatch.StartNew();

        // Act - Send 1000 sequential requests
        for (int i = 0; i < 1000; i++)
        {
            var cmd = new TestConcurrentCommand { Id = i.ToString(), Value = $"test-{i}" };
            var response = await client.PostAsJsonAsync("/api/concurrent", cmd);
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        }

        stopwatch.Stop();

        // Assert - Should complete in reasonable time (< 30 seconds for 1000 requests)
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(30000);
    }

    [Fact]
    public async Task EndpointHandler_ShouldHaveMinimalAllocationOverhead()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();
        app.RegisterEndpoint<TestPerformanceHandlers>();

        var server = new TestServer(app);
        var client = server.CreateClient();

        var cmd = new TestConcurrentCommand { Id = "1", Value = "test" };

        // Act - Warm up
        await client.PostAsJsonAsync("/api/concurrent", cmd);

        // Measure allocations
        var initialMemory = GC.GetTotalMemory(true);

        for (int i = 0; i < 100; i++)
        {
            var response = await client.PostAsJsonAsync("/api/concurrent", cmd);
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        }

        var finalMemory = GC.GetTotalMemory(true);
        var allocatedMemory = finalMemory - initialMemory;

        // Assert - Allocations should be minimal (< 10MB for 100 requests)
        allocatedMemory.Should().BeLessThan(10 * 1024 * 1024);
    }

    [Fact]
    public async Task EndpointHandler_ShouldNotHaveReflectionOverhead()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();

        // Act - RegisterEndpoint should not use reflection
        var stopwatch = Stopwatch.StartNew();
        app.RegisterEndpoint<TestPerformanceHandlers>();
        stopwatch.Stop();

        // Assert - Registration should be fast (< 100ms, no reflection)
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100);
    }

    [Fact]
    public async Task EndpointHandler_ShouldScaleLinearlyWithRequestCount()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();
        app.RegisterEndpoint<TestPerformanceHandlers>();

        var server = new TestServer(app);
        var client = server.CreateClient();

        // Act - Measure time for 100 requests
        var stopwatch1 = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            var cmd = new TestConcurrentCommand { Id = i.ToString(), Value = $"test-{i}" };
            await client.PostAsJsonAsync("/api/concurrent", cmd);
        }
        stopwatch1.Stop();

        // Act - Measure time for 200 requests
        var stopwatch2 = Stopwatch.StartNew();
        for (int i = 100; i < 300; i++)
        {
            var cmd = new TestConcurrentCommand { Id = i.ToString(), Value = $"test-{i}" };
            await client.PostAsJsonAsync("/api/concurrent", cmd);
        }
        stopwatch2.Stop();

        // Assert - Time should scale roughly linearly
        var ratio = (double)stopwatch2.ElapsedMilliseconds / stopwatch1.ElapsedMilliseconds;
        ratio.Should().BeLessThan(2.5, "Time should scale roughly linearly");
    }

    [Fact]
    public async Task EndpointHandler_ShouldHandleRapidFireRequests()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCatga();
        builder.Services.AddCatgaHandlers();

        var app = builder.Build();
        app.RegisterEndpoint<TestPerformanceHandlers>();

        var server = new TestServer(app);
        var client = server.CreateClient();

        // Act - Send requests as fast as possible
        var tasks = Enumerable.Range(0, 500)
            .Select(i => client.PostAsJsonAsync("/api/concurrent",
                new TestConcurrentCommand { Id = i.ToString(), Value = $"test-{i}" }))
            .ToList();

        var responses = await Task.WhenAll(tasks);

        // Assert
        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(System.Net.HttpStatusCode.OK));
    }
}

/// <summary>
/// Test handler for performance testing
/// </summary>
public partial class TestPerformanceHandlers
{
    [CatgaEndpoint(HttpMethod.Post, "/api/concurrent")]
    public partial async Task<IResult> HandleConcurrent(TestConcurrentCommand cmd, ICatgaMediator mediator);
}

public partial class TestPerformanceHandlers
{
    public partial async Task<IResult> HandleConcurrent(TestConcurrentCommand cmd, ICatgaMediator mediator)
    {
        var result = await mediator.SendAsync<TestConcurrentCommand, TestConcurrentResult>(cmd);
        return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest();
    }
}

// Test types
public class TestConcurrentCommand : IRequest<TestConcurrentResult>
{
    public string Id { get; set; }
    public string Value { get; set; }
}

public class TestConcurrentResult
{
    public string Id { get; set; }
    public string Value { get; set; }
    public long Timestamp { get; set; }
}
