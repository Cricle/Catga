using Catga.Cluster;
using DotNext.Net.Cluster;
using DotNext.Net.Cluster.Consensus.Raft;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Cluster;

/// <summary>
/// Unit tests for ClusterCoordinator.
/// Tests leader election, leadership tracking, and leader-only execution.
/// </summary>
public class ClusterCoordinatorTests
{
    private readonly IRaftCluster _mockCluster;
    private readonly ILogger<ClusterCoordinator> _mockLogger;
    private readonly IClusterMember _mockLeader;

    public ClusterCoordinatorTests()
    {
        _mockCluster = Substitute.For<IRaftCluster>();
        _mockLogger = Substitute.For<ILogger<ClusterCoordinator>>();
        _mockLeader = Substitute.For<IClusterMember>();
    }

    [Fact]
    public void Constructor_WithValidCluster_ShouldInitialize()
    {
        // Act
        var coordinator = new ClusterCoordinator(_mockCluster, _mockLogger);

        // Assert
        coordinator.Should().NotBeNull();
        coordinator.NodeId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Constructor_WithNullCluster_ShouldThrow()
    {
        // Act
        var act = () => new ClusterCoordinator(null!, _mockLogger);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsLeader_WhenLocalNodeIsLeader_ShouldReturnTrue()
    {
        // Arrange
        _mockLeader.IsRemote.Returns(false);
        _mockCluster.Leader.Returns(_mockLeader);
        _mockCluster.LeadershipToken.Returns(new CancellationToken(false));

        var coordinator = new ClusterCoordinator(_mockCluster, _mockLogger);

        // Act
        var isLeader = coordinator.IsLeader;

        // Assert
        isLeader.Should().BeTrue();
    }

    [Fact]
    public void IsLeader_WhenRemoteNodeIsLeader_ShouldReturnFalse()
    {
        // Arrange
        _mockLeader.IsRemote.Returns(true);
        _mockCluster.Leader.Returns(_mockLeader);
        _mockCluster.LeadershipToken.Returns(new CancellationToken(false));

        var coordinator = new ClusterCoordinator(_mockCluster, _mockLogger);

        // Act
        var isLeader = coordinator.IsLeader;

        // Assert
        isLeader.Should().BeFalse();
    }

    [Fact]
    public void IsLeader_WhenLeadershipTokenCancelled_ShouldReturnFalse()
    {
        // Arrange
        _mockLeader.IsRemote.Returns(false);
        _mockCluster.Leader.Returns(_mockLeader);
        _mockCluster.LeadershipToken.Returns(new CancellationToken(true));

        var coordinator = new ClusterCoordinator(_mockCluster, _mockLogger);

        // Act
        var isLeader = coordinator.IsLeader;

        // Assert
        isLeader.Should().BeFalse();
    }

    [Fact]
    public void LeaderEndpoint_WhenLeaderExists_ShouldReturnEndpoint()
    {
        // Arrange
        var endpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 5000);
        _mockLeader.EndPoint.Returns(endpoint);
        _mockCluster.Leader.Returns(_mockLeader);

        var coordinator = new ClusterCoordinator(_mockCluster, _mockLogger);

        // Act
        var leaderEndpoint = coordinator.LeaderEndpoint;

        // Assert
        leaderEndpoint.Should().NotBeNullOrEmpty();
        leaderEndpoint.Should().Contain("127.0.0.1");
    }

    [Fact]
    public void LeaderEndpoint_WhenNoLeader_ShouldReturnNull()
    {
        // Arrange
        _mockCluster.Leader.Returns((IClusterMember?)null);

        var coordinator = new ClusterCoordinator(_mockCluster, _mockLogger);

        // Act
        var leaderEndpoint = coordinator.LeaderEndpoint;

        // Assert
        leaderEndpoint.Should().BeNull();
    }

    [Fact]
    public async Task WaitForLeadershipAsync_WhenAlreadyLeader_ShouldReturnImmediately()
    {
        // Arrange
        _mockLeader.IsRemote.Returns(false);
        _mockCluster.Leader.Returns(_mockLeader);
        _mockCluster.LeadershipToken.Returns(new CancellationToken(false));

        var coordinator = new ClusterCoordinator(_mockCluster, _mockLogger);

        // Act
        var result = await coordinator.WaitForLeadershipAsync(TimeSpan.FromSeconds(1));

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task WaitForLeadershipAsync_WhenNotLeader_ShouldTimeout()
    {
        // Arrange
        _mockLeader.IsRemote.Returns(true);
        _mockCluster.Leader.Returns(_mockLeader);
        _mockCluster.LeadershipToken.Returns(new CancellationToken(false));

        var coordinator = new ClusterCoordinator(_mockCluster, _mockLogger);

        // Act
        var result = await coordinator.WaitForLeadershipAsync(TimeSpan.FromMilliseconds(200));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteIfLeaderAsync_WhenLeader_ShouldExecuteAction()
    {
        // Arrange
        _mockLeader.IsRemote.Returns(false);
        _mockCluster.Leader.Returns(_mockLeader);
        _mockCluster.LeadershipToken.Returns(new CancellationToken(false));

        var coordinator = new ClusterCoordinator(_mockCluster, _mockLogger);
        var executed = false;

        // Act
        var result = await coordinator.ExecuteIfLeaderAsync(async ct =>
        {
            await Task.Delay(10, ct);
            executed = true;
        });

        // Assert
        result.Should().BeTrue();
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteIfLeaderAsync_WhenNotLeader_ShouldNotExecute()
    {
        // Arrange
        _mockLeader.IsRemote.Returns(true);
        _mockCluster.Leader.Returns(_mockLeader);
        _mockCluster.LeadershipToken.Returns(new CancellationToken(false));

        var coordinator = new ClusterCoordinator(_mockCluster, _mockLogger);
        var executed = false;

        // Act
        var result = await coordinator.ExecuteIfLeaderAsync(async ct =>
        {
            await Task.Delay(10, ct);
            executed = true;
        });

        // Assert
        result.Should().BeFalse();
        executed.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteIfLeaderAsync_WithResult_WhenLeader_ShouldReturnResult()
    {
        // Arrange
        _mockLeader.IsRemote.Returns(false);
        _mockCluster.Leader.Returns(_mockLeader);
        _mockCluster.LeadershipToken.Returns(new CancellationToken(false));

        var coordinator = new ClusterCoordinator(_mockCluster, _mockLogger);

        // Act
        var (isLeader, result) = await coordinator.ExecuteIfLeaderAsync(async ct =>
        {
            await Task.Delay(10, ct);
            return 42;
        });

        // Assert
        isLeader.Should().BeTrue();
        result.Should().Be(42);
    }

    [Fact]
    public async Task ExecuteIfLeaderAsync_WithResult_WhenNotLeader_ShouldReturnDefault()
    {
        // Arrange
        _mockLeader.IsRemote.Returns(true);
        _mockCluster.Leader.Returns(_mockLeader);
        _mockCluster.LeadershipToken.Returns(new CancellationToken(false));

        var coordinator = new ClusterCoordinator(_mockCluster, _mockLogger);

        // Act
        var (isLeader, result) = await coordinator.ExecuteIfLeaderAsync(async ct =>
        {
            await Task.Delay(10, ct);
            return 42;
        });

        // Assert
        isLeader.Should().BeFalse();
        result.Should().Be(0);
    }

    [Fact]
    public void LeadershipChanged_WhenLeaderChanges_ShouldFireEvent()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(_mockCluster, _mockLogger);
        var eventFired = false;
        var isLeaderValue = false;

        coordinator.LeadershipChanged += (isLeader) =>
        {
            eventFired = true;
            isLeaderValue = isLeader;
        };

        // Act - Simulate leader change by invoking the event handler
        _mockLeader.IsRemote.Returns(false);
        _mockCluster.LeaderChanged += Raise.Event<Action<ICluster, IClusterMember?>>(_mockCluster, _mockLeader);

        // Assert
        eventFired.Should().BeTrue();
        isLeaderValue.Should().BeTrue();
    }

    [Fact]
    public void Dispose_ShouldUnsubscribeFromEvents()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(_mockCluster, _mockLogger);

        // Act
        coordinator.Dispose();

        // Assert - Should not throw when cluster raises event
        var act = () => _mockCluster.LeaderChanged += Raise.Event<Action<ICluster, IClusterMember?>>(_mockCluster, _mockLeader);
        act.Should().NotThrow();
    }
}
