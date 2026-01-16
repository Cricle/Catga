using Catga.Cluster;
using DotNext.Net.Cluster;
using DotNext.Net.Cluster.Consensus.Raft;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Catga.Tests.Cluster;

public sealed class ClusterCoordinatorTests
{
    [Fact]
    public void IsLeader_WhenLeadershipTokenNotCancelled_ReturnsTrue()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var mockCluster = CreateMockCluster(isLeader: true, cts.Token);
        var coordinator = new ClusterCoordinator(mockCluster.Object, NullLogger<ClusterCoordinator>.Instance);

        // Act
        var isLeader = coordinator.IsLeader;

        // Assert
        Assert.True(isLeader);
    }

    [Fact]
    public void IsLeader_WhenLeadershipTokenCancelled_ReturnsFalse()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var mockCluster = CreateMockCluster(isLeader: false, cts.Token);
        var coordinator = new ClusterCoordinator(mockCluster.Object, NullLogger<ClusterCoordinator>.Instance);

        // Act
        var isLeader = coordinator.IsLeader;

        // Assert
        Assert.False(isLeader);
    }

    [Fact]
    public void LeaderEndpoint_ReturnsLeaderEndpoint()
    {
        // Arrange
        var mockCluster = CreateMockCluster(isLeader: false, CancellationToken.None, "http://leader:5000");
        var coordinator = new ClusterCoordinator(mockCluster.Object);

        // Act
        var endpoint = coordinator.LeaderEndpoint;

        // Assert
        Assert.Equal("http://leader:5000", endpoint);
    }

    [Fact]
    public void NodeId_ReturnsUniqueId()
    {
        // Arrange
        var mockCluster = CreateMockCluster(isLeader: true, CancellationToken.None);
        var coordinator = new ClusterCoordinator(mockCluster.Object);

        // Act
        var nodeId = coordinator.NodeId;

        // Assert
        Assert.NotNull(nodeId);
        Assert.NotEmpty(nodeId);
    }

    [Fact]
    public async Task WaitForLeadershipAsync_WhenAlreadyLeader_ReturnsImmediately()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var mockCluster = CreateMockCluster(isLeader: true, cts.Token);
        var coordinator = new ClusterCoordinator(mockCluster.Object);

        // Act
        var result = await coordinator.WaitForLeadershipAsync(TimeSpan.FromSeconds(1));

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task WaitForLeadershipAsync_WhenNotLeader_ReturnsFalseAfterTimeout()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Not leader
        var mockCluster = CreateMockCluster(isLeader: false, cts.Token);
        var coordinator = new ClusterCoordinator(mockCluster.Object);

        // Act
        var result = await coordinator.WaitForLeadershipAsync(TimeSpan.FromMilliseconds(100));

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ExecuteIfLeaderAsync_WhenLeader_ExecutesAction()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var mockCluster = CreateMockCluster(isLeader: true, cts.Token);
        var coordinator = new ClusterCoordinator(mockCluster.Object);
        var executed = false;

        // Act
        var result = await coordinator.ExecuteIfLeaderAsync(async ct =>
        {
            await Task.Delay(10, ct);
            executed = true;
        });

        // Assert
        Assert.True(result);
        Assert.True(executed);
    }

    [Fact]
    public async Task ExecuteIfLeaderAsync_WhenNotLeader_DoesNotExecute()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var mockCluster = CreateMockCluster(isLeader: false, cts.Token);
        var coordinator = new ClusterCoordinator(mockCluster.Object);
        var executed = false;

        // Act
        var result = await coordinator.ExecuteIfLeaderAsync(async ct =>
        {
            await Task.Delay(10, ct);
            executed = true;
        });

        // Assert
        Assert.False(result);
        Assert.False(executed);
    }

    [Fact]
    public async Task ExecuteIfLeaderAsync_WithResult_WhenLeader_ReturnsResult()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var mockCluster = CreateMockCluster(isLeader: true, cts.Token);
        var coordinator = new ClusterCoordinator(mockCluster.Object);

        // Act
        var (isLeader, result) = await coordinator.ExecuteIfLeaderAsync(async ct =>
        {
            await Task.Delay(10, ct);
            return 42;
        });

        // Assert
        Assert.True(isLeader);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task ExecuteIfLeaderAsync_WithResult_WhenNotLeader_ReturnsDefault()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var mockCluster = CreateMockCluster(isLeader: false, cts.Token);
        var coordinator = new ClusterCoordinator(mockCluster.Object);

        // Act
        var (isLeader, result) = await coordinator.ExecuteIfLeaderAsync(async ct =>
        {
            await Task.Delay(10, ct);
            return 42;
        });

        // Assert
        Assert.False(isLeader);
        Assert.Equal(0, result);
    }

    [Fact]
    public void LeadershipChanged_FiresWhenLeaderChanges()
    {
        // Arrange
        var mockCluster = new Mock<IRaftCluster>();
        mockCluster.Setup(c => c.LeadershipToken).Returns(CancellationToken.None);
        
        var mockLeader = new Mock<IClusterMember>();
        mockLeader.Setup(m => m.IsRemote).Returns(false);
        mockLeader.Setup(m => m.EndPoint).Returns(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 5000));
        
        mockCluster.Setup(c => c.Leader).Returns(mockLeader.Object);

        var coordinator = new ClusterCoordinator(mockCluster.Object);
        var eventFired = false;
        var wasLeader = false;

        coordinator.LeadershipChanged += isLeader =>
        {
            eventFired = true;
            wasLeader = isLeader;
        };

        // Act
        mockCluster.Raise(c => c.LeaderChanged += null, mockCluster.Object, mockLeader.Object);

        // Assert
        Assert.True(eventFired);
        Assert.True(wasLeader);
    }

    [Fact]
    public void Dispose_UnsubscribesFromLeaderChanged()
    {
        // Arrange
        var mockCluster = new Mock<IRaftCluster>();
        mockCluster.Setup(c => c.LeadershipToken).Returns(CancellationToken.None);
        mockCluster.Setup(c => c.Leader).Returns((IClusterMember?)null);

        var coordinator = new ClusterCoordinator(mockCluster.Object);

        // Act
        coordinator.Dispose();

        // Assert - should not throw when raising event after dispose
        mockCluster.Raise(c => c.LeaderChanged += null, mockCluster.Object, null!);
    }

    private static Mock<IRaftCluster> CreateMockCluster(bool isLeader, CancellationToken leadershipToken, string? leaderEndpoint = null)
    {
        var mock = new Mock<IRaftCluster>();
        mock.Setup(c => c.LeadershipToken).Returns(leadershipToken);

        if (leaderEndpoint != null)
        {
            var mockLeader = new Mock<IClusterMember>();
            mockLeader.Setup(m => m.IsRemote).Returns(!isLeader);
            mockLeader.Setup(m => m.EndPoint).Returns(new System.Net.IPEndPoint(System.Net.IPAddress.Parse("127.0.0.1"), 5000));
            mockLeader.Setup(m => m.EndPoint!.ToString()).Returns(leaderEndpoint);
            mock.Setup(c => c.Leader).Returns(mockLeader.Object);
        }
        else
        {
            var mockLeader = new Mock<IClusterMember>();
            mockLeader.Setup(m => m.IsRemote).Returns(!isLeader);
            mock.Setup(c => c.Leader).Returns(mockLeader.Object);
        }

        return mock;
    }
}
