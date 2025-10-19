using Catga;
using Catga.Core;
using Catga.Exceptions;
using Catga.Messages;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Catga.Tests.Handlers;

/// <summary>
/// Tests for SafeRequestHandler custom error handling methods
/// </summary>
public class SafeRequestHandlerCustomErrorTests
{
    [Fact]
    public async Task OnBusinessErrorAsync_DefaultImplementation_ShouldReturnFailureResult()
    {
        // Arrange
        var handler = new DefaultErrorHandler(NullLogger<DefaultErrorHandler>.Instance);
        var request = new TestRequest("test");

        // Act
        var result = await handler.PublicOnBusinessErrorAsync(
            request,
            new CatgaException("Business error"),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Business error");
    }

    [Fact]
    public async Task OnUnexpectedErrorAsync_DefaultImplementation_ShouldReturnInternalError()
    {
        // Arrange
        var handler = new DefaultErrorHandler(NullLogger<DefaultErrorHandler>.Instance);
        var request = new TestRequest("test");

        // Act
        var result = await handler.PublicOnUnexpectedErrorAsync(
            request,
            new InvalidOperationException("Unexpected error"),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Internal error");
    }

    [Fact]
    public async Task CustomBusinessErrorHandler_ShouldOverrideDefaultBehavior()
    {
        // Arrange
        var handler = new CustomBusinessErrorHandler(NullLogger<CustomBusinessErrorHandler>.Instance);
        var request = new TestRequest("test-id");

        // Act
        var result = await handler.HandleAsync(request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Custom business error handling");
        result.Error.Should().Contain("test-id");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.ContainsKey("CustomErrorType").Should().BeTrue();
        result.Metadata.TryGetValue("CustomErrorType", out var errorType).Should().BeTrue();
        errorType!.Should().Be("Business");
    }

    [Fact]
    public async Task CustomUnexpectedErrorHandler_ShouldOverrideDefaultBehavior()
    {
        // Arrange
        var handler = new CustomUnexpectedErrorHandler(NullLogger<CustomUnexpectedErrorHandler>.Instance);
        var request = new TestRequest("test-id");

        // Act
        var result = await handler.HandleAsync(request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Custom unexpected error handling");
        result.Error.Should().Contain("test-id");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.ContainsKey("CustomErrorType").Should().BeTrue();
        result.Metadata.TryGetValue("CustomErrorType", out var errorType).Should().BeTrue();
        errorType!.Should().Be("Unexpected");
        result.Metadata.ContainsKey("OriginalException").Should().BeTrue();
    }

    [Fact]
    public async Task CustomErrorHandler_WithEnrichment_ShouldAddMetadata()
    {
        // Arrange
        var handler = new EnrichedErrorHandler(NullLogger<EnrichedErrorHandler>.Instance);
        var request = new TestRequest("order-123");

        // Act
        var result = await handler.HandleAsync(request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.ContainsKey("RequestId").Should().BeTrue();
        result.Metadata.TryGetValue("RequestId", out var requestId).Should().BeTrue();
        requestId!.Should().Be("order-123");
        result.Metadata.ContainsKey("Timestamp").Should().BeTrue();
        result.Metadata.ContainsKey("ErrorCategory").Should().BeTrue();
    }

    [Fact]
    public async Task NoResponseHandler_DefaultError_ShouldWork()
    {
        // Arrange
        var handler = new NoResponseDefaultHandler(NullLogger<NoResponseDefaultHandler>.Instance);
        var request = new NoResponseRequest("test");

        // Act
        var result = await handler.HandleAsync(request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task NoResponseHandler_CustomError_ShouldOverride()
    {
        // Arrange
        var handler = new NoResponseCustomHandler(NullLogger<NoResponseCustomHandler>.Instance);
        var request = new NoResponseRequest("test-id");

        // Act
        var result = await handler.HandleAsync(request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Custom no-response error");
        result.Error.Should().Contain("test-id");
    }

    [Fact]
    public async Task CustomErrorHandler_CanAccessRequest_ShouldUseRequestData()
    {
        // Arrange
        var handler = new RequestAwareErrorHandler(NullLogger<RequestAwareErrorHandler>.Instance);
        var request = new TestRequest("important-data");

        // Act
        var result = await handler.HandleAsync(request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("important-data");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.ContainsKey("RequestData").Should().BeTrue();
        result.Metadata.TryGetValue("RequestData", out var requestData).Should().BeTrue();
        requestData!.Should().Be("important-data");
    }

    // Test Messages
    private record TestRequest(string Id) : IRequest<TestResponse>
    {
        public string MessageId { get; init; } = MessageExtensions.NewMessageId();
    }
    private record TestResponse(string Result);

    // Handler that exposes protected methods for testing
    private class DefaultErrorHandler : SafeRequestHandler<TestRequest, TestResponse>
    {
        public DefaultErrorHandler(ILogger<DefaultErrorHandler> logger) : base(logger) { }

        protected override Task<TestResponse> HandleCoreAsync(TestRequest request, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<CatgaResult<TestResponse>> PublicOnBusinessErrorAsync(TestRequest request, CatgaException exception, CancellationToken cancellationToken)
            => OnBusinessErrorAsync(request, exception, cancellationToken);

        public Task<CatgaResult<TestResponse>> PublicOnUnexpectedErrorAsync(TestRequest request, Exception exception, CancellationToken cancellationToken)
            => OnUnexpectedErrorAsync(request, exception, cancellationToken);
    }

    // Handler with custom business error handling
    private class CustomBusinessErrorHandler : SafeRequestHandler<TestRequest, TestResponse>
    {
        public CustomBusinessErrorHandler(ILogger<CustomBusinessErrorHandler> logger) : base(logger) { }

        protected override Task<TestResponse> HandleCoreAsync(TestRequest request, CancellationToken cancellationToken)
        {
            throw new CatgaException("Business failure");
        }

        protected override Task<CatgaResult<TestResponse>> OnBusinessErrorAsync(TestRequest request, CatgaException exception, CancellationToken cancellationToken)
        {
            // Custom error handling
            var metadata = new ResultMetadata();
            metadata.Add("CustomErrorType", "Business");
            metadata.Add("RequestId", request.Id);

            var result = new CatgaResult<TestResponse>
            {
                IsSuccess = false,
                Error = $"Custom business error handling for request {request.Id}: {exception.Message}",
                Exception = exception,
                Metadata = metadata
            };

            return Task.FromResult(result);
        }
    }

    // Handler with custom unexpected error handling
    private class CustomUnexpectedErrorHandler : SafeRequestHandler<TestRequest, TestResponse>
    {
        public CustomUnexpectedErrorHandler(ILogger<CustomUnexpectedErrorHandler> logger) : base(logger) { }

        protected override Task<TestResponse> HandleCoreAsync(TestRequest request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Unexpected failure");
        }

        protected override Task<CatgaResult<TestResponse>> OnUnexpectedErrorAsync(TestRequest request, Exception exception, CancellationToken cancellationToken)
        {
            // Custom unexpected error handling
            var metadata = new ResultMetadata();
            metadata.Add("CustomErrorType", "Unexpected");
            metadata.Add("RequestId", request.Id);
            metadata.Add("OriginalException", exception.GetType().Name);

            var result = new CatgaResult<TestResponse>
            {
                IsSuccess = false,
                Error = $"Custom unexpected error handling for request {request.Id}: {exception.Message}",
                Exception = new CatgaException("Custom wrapped error", exception),
                Metadata = metadata
            };

            return Task.FromResult(result);
        }
    }

    // Handler that enriches errors with metadata
    private class EnrichedErrorHandler : SafeRequestHandler<TestRequest, TestResponse>
    {
        public EnrichedErrorHandler(ILogger<EnrichedErrorHandler> logger) : base(logger) { }

        protected override Task<TestResponse> HandleCoreAsync(TestRequest request, CancellationToken cancellationToken)
        {
            throw new CatgaException("Some error");
        }

        protected override Task<CatgaResult<TestResponse>> OnBusinessErrorAsync(TestRequest request, CatgaException exception, CancellationToken cancellationToken)
        {
            var metadata = new ResultMetadata();
            metadata.Add("RequestId", request.Id);
            metadata.Add("Timestamp", DateTime.UtcNow.ToString("O"));
            metadata.Add("ErrorCategory", "Business");
            metadata.Add("Severity", "Warning");

            var result = new CatgaResult<TestResponse>
            {
                IsSuccess = false,
                Error = exception.Message,
                Exception = exception,
                Metadata = metadata
            };

            return Task.FromResult(result);
        }
    }

    // Additional message type for no-response tests
    private record NoResponseRequest(string Id) : IRequest
    {
        public string MessageId { get; init; } = MessageExtensions.NewMessageId();
    }

    // No-response handler with default error handling
    private class NoResponseDefaultHandler : SafeRequestHandler<NoResponseRequest>
    {
        public NoResponseDefaultHandler(ILogger<NoResponseDefaultHandler> logger) : base(logger) { }

        protected override Task HandleCoreAsync(NoResponseRequest request, CancellationToken cancellationToken)
        {
            throw new CatgaException("No-response error");
        }
    }

    // No-response handler with custom error handling
    private class NoResponseCustomHandler : SafeRequestHandler<NoResponseRequest>
    {
        public NoResponseCustomHandler(ILogger<NoResponseCustomHandler> logger) : base(logger) { }

        protected override Task HandleCoreAsync(NoResponseRequest request, CancellationToken cancellationToken)
        {
            throw new CatgaException("Error");
        }

        protected override Task<CatgaResult> OnBusinessErrorAsync(NoResponseRequest request, CatgaException exception, CancellationToken cancellationToken)
        {
            var result = CatgaResult.Failure($"Custom no-response error for {request.Id}: {exception.Message}", exception);
            return Task.FromResult(result);
        }
    }

    // Handler that uses request data in error messages
    private class RequestAwareErrorHandler : SafeRequestHandler<TestRequest, TestResponse>
    {
        public RequestAwareErrorHandler(ILogger<RequestAwareErrorHandler> logger) : base(logger) { }

        protected override Task<TestResponse> HandleCoreAsync(TestRequest request, CancellationToken cancellationToken)
        {
            throw new CatgaException("Error");
        }

        protected override Task<CatgaResult<TestResponse>> OnBusinessErrorAsync(TestRequest request, CatgaException exception, CancellationToken cancellationToken)
        {
            // Use request data to create contextual error message
            var metadata = new ResultMetadata();
            metadata.Add("RequestData", request.Id);

            var result = new CatgaResult<TestResponse>
            {
                IsSuccess = false,
                Error = $"Failed to process request with data: {request.Id}",
                Exception = exception,
                Metadata = metadata
            };

            return Task.FromResult(result);
        }
    }
}

