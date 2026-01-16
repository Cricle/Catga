using System.Net;
using System.Text.Json;
using Catga.Abstractions;
using Catga.Cluster;
using Moq;
using Moq.Protected;
using Xunit;

namespace Catga.Tests.Cluster;

public sealed class HttpClusterForwarderTests
{
    private record TestCommand(string Data) : CommandBase, IRequest<TestResponse>;
    private record TestResponse(string Result);

    [Fact]
    public async Task ForwardAsync_Success_ReturnsSuccessResult()
    {
        // Arrange
        var response = new TestResponse("success");
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(response))
        };

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);

        var httpClient = new HttpClient(mockHandler.Object);
        var forwarder = new HttpClusterForwarder(httpClient);

        var command = new TestCommand("test");

        // Act
        var result = await forwarder.ForwardAsync<TestCommand, TestResponse>(
            command,
            "http://leader:5000",
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("success", result.Value.Result);
    }

    [Fact]
    public async Task ForwardAsync_HttpError_ReturnsFailure()
    {
        // Arrange
        var httpResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Server error")
        };

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);

        var httpClient = new HttpClient(mockHandler.Object);
        var forwarder = new HttpClusterForwarder(httpClient);

        var command = new TestCommand("test");

        // Act
        var result = await forwarder.ForwardAsync<TestCommand, TestResponse>(
            command,
            "http://leader:5000",
            CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Forward failed", result.Error);
    }

    [Fact]
    public async Task ForwardAsync_NetworkError_ReturnsFailure()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var httpClient = new HttpClient(mockHandler.Object);
        var forwarder = new HttpClusterForwarder(httpClient);

        var command = new TestCommand("test");

        // Act
        var result = await forwarder.ForwardAsync<TestCommand, TestResponse>(
            command,
            "http://leader:5000",
            CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Network error", result.Error);
    }

    [Fact]
    public async Task ForwardAsync_EmptyResponse_ReturnsFailure()
    {
        // Arrange
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null")
        };

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);

        var httpClient = new HttpClient(mockHandler.Object);
        var forwarder = new HttpClusterForwarder(httpClient);

        var command = new TestCommand("test");

        // Act
        var result = await forwarder.ForwardAsync<TestCommand, TestResponse>(
            command,
            "http://leader:5000",
            CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Empty response", result.Error);
    }

    [Fact]
    public async Task ForwardAsync_UsesCorrectUrl()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        var response = new TestResponse("success");
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(response))
        };

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(httpResponse);

        var httpClient = new HttpClient(mockHandler.Object);
        var forwarder = new HttpClusterForwarder(httpClient);

        var command = new TestCommand("test");

        // Act
        await forwarder.ForwardAsync<TestCommand, TestResponse>(
            command,
            "http://leader:5000",
            CancellationToken.None);

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Equal("http://leader:5000/api/catga/forward/TestCommand", capturedRequest.RequestUri?.ToString());
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
    }
}
