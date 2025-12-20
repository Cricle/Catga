using System.Diagnostics;
using Catga.DependencyInjection;
using FluentAssertions;

namespace Catga.Tests.DependencyInjection;

/// <summary>
/// Comprehensive tests for CorrelationIdDelegatingHandler
/// </summary>
public class CorrelationIdDelegatingHandlerTests
{
    private const string CorrelationIdHeaderName = "X-Correlation-ID";
    private const string BaggageKey = "catga.correlation_id";

    private class TestHandler : DelegatingHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task SendAsync_WithCorrelationIdInBaggage_ShouldAddHeader()
    {
        // Arrange
        var testHandler = new TestHandler();
        var handler = new CorrelationIdDelegatingHandler
        {
            InnerHandler = testHandler
        };
        var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://test.com/api");

        using var activity = new Activity("Test");
        activity.AddBaggage(BaggageKey, "test-correlation-123");
        activity.Start();

        // Act
        await client.SendAsync(request);

        // Assert
        testHandler.LastRequest.Should().NotBeNull();
        testHandler.LastRequest!.Headers.Contains(CorrelationIdHeaderName).Should().BeTrue();
        testHandler.LastRequest.Headers.GetValues(CorrelationIdHeaderName).First().Should().Be("test-correlation-123");
    }

    [Fact]
    public async Task SendAsync_WithoutActivity_ShouldNotAddHeader()
    {
        // Arrange
        var testHandler = new TestHandler();
        var handler = new CorrelationIdDelegatingHandler
        {
            InnerHandler = testHandler
        };
        var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://test.com/api");

        // Ensure no activity is current
        Activity.Current = null;

        // Act
        await client.SendAsync(request);

        // Assert
        testHandler.LastRequest.Should().NotBeNull();
        testHandler.LastRequest!.Headers.Contains(CorrelationIdHeaderName).Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_WithActivityButNoCorrelationId_ShouldNotAddHeader()
    {
        // Arrange
        var testHandler = new TestHandler();
        var handler = new CorrelationIdDelegatingHandler
        {
            InnerHandler = testHandler
        };
        var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://test.com/api");

        using var activity = new Activity("Test");
        activity.Start(); // No baggage added

        // Act
        await client.SendAsync(request);

        // Assert
        testHandler.LastRequest.Should().NotBeNull();
        testHandler.LastRequest!.Headers.Contains(CorrelationIdHeaderName).Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_WithExistingHeader_ShouldNotOverwrite()
    {
        // Arrange
        var testHandler = new TestHandler();
        var handler = new CorrelationIdDelegatingHandler
        {
            InnerHandler = testHandler
        };
        var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://test.com/api");
        request.Headers.Add(CorrelationIdHeaderName, "existing-correlation-id");

        using var activity = new Activity("Test");
        activity.AddBaggage(BaggageKey, "new-correlation-id");
        activity.Start();

        // Act
        await client.SendAsync(request);

        // Assert
        testHandler.LastRequest.Should().NotBeNull();
        testHandler.LastRequest!.Headers.GetValues(CorrelationIdHeaderName).Should().ContainSingle()
            .Which.Should().Be("existing-correlation-id");
    }

    [Fact]
    public async Task SendAsync_WithEmptyCorrelationId_ShouldNotAddHeader()
    {
        // Arrange
        var testHandler = new TestHandler();
        var handler = new CorrelationIdDelegatingHandler
        {
            InnerHandler = testHandler
        };
        var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://test.com/api");

        using var activity = new Activity("Test");
        activity.AddBaggage(BaggageKey, "");
        activity.Start();

        // Act
        await client.SendAsync(request);

        // Assert
        testHandler.LastRequest.Should().NotBeNull();
        testHandler.LastRequest!.Headers.Contains(CorrelationIdHeaderName).Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_ShouldReturnResponse()
    {
        // Arrange
        var testHandler = new TestHandler();
        var handler = new CorrelationIdDelegatingHandler
        {
            InnerHandler = testHandler
        };
        var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://test.com/api");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task SendAsync_WithCancellationToken_ShouldPassThrough()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var testHandler = new TestHandler();
        var handler = new CorrelationIdDelegatingHandler
        {
            InnerHandler = testHandler
        };
        var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://test.com/api");

        // Act
        var response = await client.SendAsync(request, cts.Token);

        // Assert
        response.Should().NotBeNull();
    }
}
