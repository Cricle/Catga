using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using YourNamespace.Commands;

namespace YourNamespace.Tests;

public class SampleHandlerCommandHandlerTests
{
    private readonly SampleHandlerCommandHandler _handler;

    public SampleHandlerCommandHandlerTests()
    {
        _handler = new SampleHandlerCommandHandler(NullLogger<SampleHandlerCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ValidCommand_ReturnsResponse()
    {
        // Arrange
        var command = new SampleHandlerCommand("Test", 42);

        // Act
        var response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.Id > 0);
        Assert.Contains("Test", response.Message);
        Assert.Contains("42", response.Message);
        Assert.True(response.Timestamp <= DateTime.UtcNow);
    }

    [Fact]
    public async Task Handle_WithDifferentValues_ReturnsUniqueIds()
    {
        // Arrange
        var command1 = new SampleHandlerCommand("First", 1);
        var command2 = new SampleHandlerCommand("Second", 2);

        // Act
        var response1 = await _handler.Handle(command1, CancellationToken.None);
        var response2 = await _handler.Handle(command2, CancellationToken.None);

        // Assert
        Assert.NotEqual(response1.Id, response2.Id);
    }

    [Fact]
    public async Task Handle_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var command = new SampleHandlerCommand("Test", 42);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _handler.Handle(command, cts.Token).AsTask());
    }
}

public class SampleHandlerValidatorTests
{
    private readonly SampleHandlerValidator _validator;

    public SampleHandlerValidatorTests()
    {
        _validator = new SampleHandlerValidator();
    }

    [Fact]
    public async Task Validate_EmptyName_ThrowsValidationException()
    {
        // Arrange
        var command = new SampleHandlerCommand("", 42);
        RequestHandlerDelegate<SampleHandlerResponse> next = () => 
            ValueTask.FromResult(new SampleHandlerResponse(1, "Test", DateTime.UtcNow));

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(
            () => _validator.Handle(command, next, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Validate_NegativeValue_ThrowsValidationException()
    {
        // Arrange
        var command = new SampleHandlerCommand("Test", -1);
        RequestHandlerDelegate<SampleHandlerResponse> next = () => 
            ValueTask.FromResult(new SampleHandlerResponse(1, "Test", DateTime.UtcNow));

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(
            () => _validator.Handle(command, next, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Validate_ValidCommand_CallsNext()
    {
        // Arrange
        var command = new SampleHandlerCommand("Test", 42);
        var expectedResponse = new SampleHandlerResponse(1, "Test", DateTime.UtcNow);
        RequestHandlerDelegate<SampleHandlerResponse> next = () => ValueTask.FromResult(expectedResponse);

        // Act
        var result = await _validator.Handle(command, next, CancellationToken.None);

        // Assert
        Assert.Equal(expectedResponse, result);
    }
}

