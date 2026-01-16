using Catga.Abstractions;
using Catga.Cluster;
using Catga.Core;
using Catga.Pipeline;
using Moq;
using Xunit;

namespace Catga.Tests.Cluster;

public sealed class ForwardToLeaderBehaviorTests
{
    private record TestCommand(string Data) : CommandBase, IRequest<TestResponse>;
    private record TestResponse(string Result);

    [Fact]
    public async Task HandleAsync_WhenLeader_CallsNext()
    {
        // Arrange
        var mockCoordinator = new Mock<IClusterCoordinator>();
        mockCoordinator.Setup(c => c.IsLeader).Returns(true);

        var behavior = new ForwardToLeaderBehavior<TestCommand, TestResponse>(mockCoordinator.Object);
        var command = new TestCommand("test");
        var nextCalled = false;

        PipelineDelegate<TestResponse> next = () =>
        {
            nextCalled = true;
            return ValueTask.FromResult(CatgaResult<TestResponse>.Success(new TestResponse("ok")));
        };

        // Act
        var result = await behavior.HandleAsync(command, next);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task HandleAsync_WhenNotLeaderAndNoForwarder_ReturnsFailure()
    {
        // Arrange
        var mockCoordinator = new Mock<IClusterCoordinator>();
        mockCoordinator.Setup(c => c.IsLeader).Returns(false);

        var behavior = new ForwardToLeaderBehavior<TestCommand, TestResponse>(mockCoordinator.Object, null);
        var command = new TestCommand("test");

        PipelineDelegate<TestResponse> next = () =>
            ValueTask.FromResult(CatgaResult<TestResponse>.Success(new TestResponse("ok")));

        // Act
        var result = await behavior.HandleAsync(command, next);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("no forwarder available", result.Error);
    }

    [Fact]
    public async Task HandleAsync_WhenNotLeaderWithForwarder_ForwardsRequest()
    {
        // Arrange
        var mockCoordinator = new Mock<IClusterCoordinator>();
        mockCoordinator.Setup(c => c.IsLeader).Returns(false);
        mockCoordinator.Setup(c => c.LeaderEndpoint).Returns("http://leader:5000");

        var mockForwarder = new Mock<IClusterForwarder>();
        mockForwarder
            .Setup(f => f.ForwardAsync<TestCommand, TestResponse>(
                It.IsAny<TestCommand>(),
                "http://leader:5000",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CatgaResult<TestResponse>.Success(new TestResponse("ok")));

        var behavior = new ForwardToLeaderBehavior<TestCommand, TestResponse>(
            mockCoordinator.Object,
            mockForwarder.Object);
        var command = new TestCommand("test");

        PipelineDelegate<TestResponse> next = () =>
            ValueTask.FromResult(CatgaResult<TestResponse>.Success(new TestResponse("ok")));

        // Act
        var result = await behavior.HandleAsync(command, next);

        // Assert
        Assert.True(result.IsSuccess);
        mockForwarder.Verify(f => f.ForwardAsync<TestCommand, TestResponse>(
            command,
            "http://leader:5000",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenForwardFails_ReturnsFailure()
    {
        // Arrange
        var mockCoordinator = new Mock<IClusterCoordinator>();
        mockCoordinator.Setup(c => c.IsLeader).Returns(false);
        mockCoordinator.Setup(c => c.LeaderEndpoint).Returns("http://leader:5000");

        var mockForwarder = new Mock<IClusterForwarder>();
        mockForwarder
            .Setup(f => f.ForwardAsync<TestCommand, TestResponse>(
                It.IsAny<TestCommand>(),
                "http://leader:5000",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CatgaResult<TestResponse>.Failure("Network error"));

        var behavior = new ForwardToLeaderBehavior<TestCommand, TestResponse>(
            mockCoordinator.Object,
            mockForwarder.Object);
        var command = new TestCommand("test");

        PipelineDelegate<TestResponse> next = () =>
            ValueTask.FromResult(CatgaResult<TestResponse>.Success(new TestResponse("ok")));

        // Act
        var result = await behavior.HandleAsync(command, next);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Network error", result.Error);
    }
}
