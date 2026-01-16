using Catga.Abstractions;
using Catga.Cluster;
using Catga.Core;
using Catga.Pipeline;
using Moq;
using Xunit;

namespace Catga.Tests.Cluster;

public sealed class LeaderOnlyBehaviorTests
{
    private record TestCommand(string Data) : CommandBase, IRequest<TestResponse>;
    private record TestResponse(string Result);

    [Fact]
    public async Task HandleAsync_WhenLeader_CallsNext()
    {
        // Arrange
        var mockCoordinator = new Mock<IClusterCoordinator>();
        mockCoordinator.Setup(c => c.IsLeader).Returns(true);

        var behavior = new LeaderOnlyBehavior<TestCommand, TestResponse>(mockCoordinator.Object);
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
    public async Task HandleAsync_WhenNotLeader_ReturnsFailure()
    {
        // Arrange
        var mockCoordinator = new Mock<IClusterCoordinator>();
        mockCoordinator.Setup(c => c.IsLeader).Returns(false);
        mockCoordinator.Setup(c => c.LeaderEndpoint).Returns("http://leader:5000");

        var behavior = new LeaderOnlyBehavior<TestCommand, TestResponse>(mockCoordinator.Object);
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
        Assert.False(result.IsSuccess);
        Assert.False(nextCalled);
        Assert.Contains("not the leader", result.Error);
    }
}
