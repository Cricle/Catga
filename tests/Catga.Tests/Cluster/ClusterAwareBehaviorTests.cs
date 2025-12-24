using Catga.Abstractions;
using Catga.Cluster;
using Catga.Core;
using Catga.Pipeline;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Cluster;

/// <summary>
/// Unit tests for ClusterAwareBehavior classes.
/// Tests LeaderOnlyBehavior and ForwardToLeaderBehavior.
/// </summary>
public class ClusterAwareBehaviorTests
{
    private readonly IClusterCoordinator _mockCoordinator;

    public ClusterAwareBehaviorTests()
    {
        _mockCoordinator = Substitute.For<IClusterCoordinator>();
    }

    #region LeaderOnlyBehavior Tests

    [Fact]
    public async Task LeaderOnlyBehavior_WhenLeader_ShouldExecuteNext()
    {
        // Arrange
        _mockCoordinator.IsLeader.Returns(true);
        var behavior = new LeaderOnlyBehavior<TestCommand, TestResponse>(_mockCoordinator);

        var nextCalled = false;
        PipelineDelegate<TestResponse> next = () =>
        {
            nextCalled = true;
            return ValueTask.FromResult(CatgaResult<TestResponse>.Success(new TestResponse { Value = "Success" }));
        };

        var request = new TestCommand();

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Value.Should().Be("Success");
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task LeaderOnlyBehavior_WhenNotLeader_ShouldReturnFailure()
    {
        // Arrange
        _mockCoordinator.IsLeader.Returns(false);
        _mockCoordinator.LeaderEndpoint.Returns("leader-node:5000");
        var behavior = new LeaderOnlyBehavior<TestCommand, TestResponse>(_mockCoordinator);

        var nextCalled = false;
        PipelineDelegate<TestResponse> next = () =>
        {
            nextCalled = true;
            return ValueTask.FromResult(CatgaResult<TestResponse>.Success(new TestResponse()));
        };

        var request = new TestCommand();

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not the leader");
        result.Error.Should().Contain("leader-node:5000");
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task LeaderOnlyBehavior_WhenNotLeaderAndNoLeaderEndpoint_ShouldReturnFailureWithUnknown()
    {
        // Arrange
        _mockCoordinator.IsLeader.Returns(false);
        _mockCoordinator.LeaderEndpoint.Returns((string?)null);
        var behavior = new LeaderOnlyBehavior<TestCommand, TestResponse>(_mockCoordinator);

        PipelineDelegate<TestResponse> next = () =>
            ValueTask.FromResult(CatgaResult<TestResponse>.Success(new TestResponse()));

        var request = new TestCommand();

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("unknown");
    }

    #endregion

    #region ForwardToLeaderBehavior Tests

    [Fact]
    public async Task ForwardToLeaderBehavior_WhenLeader_ShouldExecuteLocally()
    {
        // Arrange
        _mockCoordinator.IsLeader.Returns(true);
        var behavior = new ForwardToLeaderBehavior<TestCommand, TestResponse>(_mockCoordinator);

        var nextCalled = false;
        PipelineDelegate<TestResponse> next = () =>
        {
            nextCalled = true;
            return ValueTask.FromResult(CatgaResult<TestResponse>.Success(new TestResponse { Value = "Local" }));
        };

        var request = new TestCommand();

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Value.Should().Be("Local");
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task ForwardToLeaderBehavior_WhenNotLeaderAndNoForwarder_ShouldReturnFailure()
    {
        // Arrange
        _mockCoordinator.IsLeader.Returns(false);
        var behavior = new ForwardToLeaderBehavior<TestCommand, TestResponse>(_mockCoordinator, forwarder: null);

        PipelineDelegate<TestResponse> next = () =>
            ValueTask.FromResult(CatgaResult<TestResponse>.Success(new TestResponse()));

        var request = new TestCommand();

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Not leader");
        result.Error.Should().Contain("no forwarder");
    }

    [Fact]
    public async Task ForwardToLeaderBehavior_WhenNotLeaderAndNoLeaderEndpoint_ShouldReturnFailure()
    {
        // Arrange
        _mockCoordinator.IsLeader.Returns(false);
        _mockCoordinator.LeaderEndpoint.Returns((string?)null);

        var mockForwarder = Substitute.For<IClusterForwarder>();
        var behavior = new ForwardToLeaderBehavior<TestCommand, TestResponse>(_mockCoordinator, mockForwarder);

        PipelineDelegate<TestResponse> next = () =>
            ValueTask.FromResult(CatgaResult<TestResponse>.Success(new TestResponse()));

        var request = new TestCommand();

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("no forwarder");
    }

    [Fact]
    public async Task ForwardToLeaderBehavior_WhenNotLeaderWithForwarder_ShouldForwardRequest()
    {
        // Arrange
        _mockCoordinator.IsLeader.Returns(false);
        _mockCoordinator.LeaderEndpoint.Returns("leader-node:5000");

        var mockForwarder = Substitute.For<IClusterForwarder>();
        mockForwarder.ForwardAsync<TestCommand, TestResponse>(
            Arg.Any<TestCommand>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(CatgaResult<TestResponse>.Success(new TestResponse { Value = "Forwarded" }));

        var behavior = new ForwardToLeaderBehavior<TestCommand, TestResponse>(_mockCoordinator, mockForwarder);

        var nextCalled = false;
        PipelineDelegate<TestResponse> next = () =>
        {
            nextCalled = true;
            return ValueTask.FromResult(CatgaResult<TestResponse>.Success(new TestResponse()));
        };

        var request = new TestCommand();

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Value.Should().Be("Forwarded");
        nextCalled.Should().BeFalse();

        await mockForwarder.Received(1).ForwardAsync<TestCommand, TestResponse>(
            request,
            "leader-node:5000",
            Arg.Any<CancellationToken>());
    }

    #endregion

    // Test types
    private record TestCommand : IRequest<TestResponse>
    {
        public long MessageId { get; init; } = DateTimeOffset.UtcNow.Ticks;
    }

    private record TestResponse
    {
        public string Value { get; init; } = string.Empty;
    }
}
