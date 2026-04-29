using Catga.Cluster;
using DotNext.Net.Cluster;
using DotNext.Net.Cluster.Consensus.Raft;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Net;
using Xunit;

namespace Catga.Tests.Cluster;

/// <summary>
/// Tests for the enhanced IClusterCoordinator: MemberEndpoints and ClusterSize.
/// </summary>
public class ClusterEnhancedTests
{
    // ── IClusterCoordinator interface ─────────────────────────────────────────

    [Fact]
    public void IClusterCoordinator_HasMemberEndpoints()
    {
        typeof(IClusterCoordinator).GetProperty("MemberEndpoints").Should().NotBeNull();
    }

    [Fact]
    public void IClusterCoordinator_HasClusterSize()
    {
        typeof(IClusterCoordinator).GetProperty("ClusterSize").Should().NotBeNull();
    }

    [Fact]
    public void IClusterCoordinator_MemberEndpoints_ReturnsReadOnlyList()
    {
        var prop = typeof(IClusterCoordinator).GetProperty("MemberEndpoints")!;
        prop.PropertyType.Should().BeAssignableTo(typeof(IReadOnlyList<string>));
    }

    [Fact]
    public void IClusterCoordinator_ClusterSize_ReturnsInt()
    {
        var prop = typeof(IClusterCoordinator).GetProperty("ClusterSize")!;
        prop.PropertyType.Should().Be(typeof(int));
    }

    // ── ClusterCoordinator.MemberEndpoints ────────────────────────────────────

    [Fact]
    public void MemberEndpoints_WithNoMembers_ReturnsEmpty()
    {
        var mockCluster = CreateMockCluster([]);
        var coordinator = new ClusterCoordinator(mockCluster.Object, NullLogger<ClusterCoordinator>.Instance);

        coordinator.MemberEndpoints.Should().BeEmpty();
    }

    [Fact]
    public void MemberEndpoints_WithThreeMembers_ReturnsThreeEndpoints()
    {
        var members = new[]
        {
            CreateRaftMember("http://node1:5000"),
            CreateRaftMember("http://node2:5001"),
            CreateRaftMember("http://node3:5002")
        };
        var mockCluster = CreateMockCluster(members);
        var coordinator = new ClusterCoordinator(mockCluster.Object, NullLogger<ClusterCoordinator>.Instance);

        coordinator.MemberEndpoints.Should().HaveCount(3);
    }

    [Fact]
    public void MemberEndpoints_ExcludesMembersWithNullEndpoint()
    {
        var memberWithNull = new Mock<IRaftClusterMember>();
        memberWithNull.Setup(m => m.EndPoint).Returns((EndPoint?)null!);

        var memberWithEndpoint = CreateRaftMember("http://node1:5000");

        var mockCluster = CreateMockCluster([memberWithNull.Object, memberWithEndpoint]);
        var coordinator = new ClusterCoordinator(mockCluster.Object, NullLogger<ClusterCoordinator>.Instance);

        coordinator.MemberEndpoints.Should().HaveCount(1);
    }

    // ── ClusterCoordinator.ClusterSize ────────────────────────────────────────

    [Fact]
    public void ClusterSize_WithNoMembers_ReturnsZero()
    {
        var mockCluster = CreateMockCluster([]);
        var coordinator = new ClusterCoordinator(mockCluster.Object, NullLogger<ClusterCoordinator>.Instance);

        coordinator.ClusterSize.Should().Be(0);
    }

    [Fact]
    public void ClusterSize_WithThreeMembers_ReturnsThree()
    {
        var members = new[]
        {
            CreateRaftMember("http://node1:5000"),
            CreateRaftMember("http://node2:5001"),
            CreateRaftMember("http://node3:5002")
        };
        var mockCluster = CreateMockCluster(members);
        var coordinator = new ClusterCoordinator(mockCluster.Object, NullLogger<ClusterCoordinator>.Instance);

        coordinator.ClusterSize.Should().Be(3);
    }

    [Fact]
    public void ClusterSize_WithSingleMember_ReturnsOne()
    {
        var mockCluster = CreateMockCluster([CreateRaftMember("http://node1:5000")]);
        var coordinator = new ClusterCoordinator(mockCluster.Object, NullLogger<ClusterCoordinator>.Instance);

        coordinator.ClusterSize.Should().Be(1);
    }

    // ── RaftClusterConfiguration ──────────────────────────────────────────────

    [Fact]
    public void RaftClusterConfiguration_CreateLocalCluster_HasCorrectMemberCount()
    {
        var config = RaftClusterConfiguration.CreateLocalCluster(nodeId: 0, totalNodes: 3);

        config.Members.Should().HaveCount(2); // 3 total - 1 self = 2 others
        config.LocalNodeEndpoint.Should().Contain("5000");
    }

    [Fact]
    public void RaftClusterConfiguration_CreateLocalCluster_MembersExcludeSelf()
    {
        var config = RaftClusterConfiguration.CreateLocalCluster(nodeId: 1, totalNodes: 3, basePort: 6000);

        config.LocalNodeEndpoint.Should().Contain("6001");
        config.Members.Should().NotContain(m => m.Contains("6001"));
    }

    [Fact]
    public void RaftClusterConfiguration_DefaultTimeouts_AreReasonable()
    {
        var config = RaftClusterConfiguration.CreateLocalCluster(0, 3);

        config.ElectionTimeout.Should().BeGreaterThan(TimeSpan.Zero);
        config.HeartbeatInterval.Should().BeGreaterThan(TimeSpan.Zero);
        config.ElectionTimeout.Should().BeGreaterThan(config.HeartbeatInterval);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IRaftClusterMember CreateRaftMember(string endpoint)
    {
        var mock = new Mock<IRaftClusterMember>();
        mock.Setup(m => m.EndPoint).Returns(new DnsEndPoint("localhost", 5000));
        mock.Setup(m => m.EndPoint!.ToString()).Returns(endpoint);
        return mock.Object;
    }

    private static Mock<IRaftCluster> CreateMockCluster(IReadOnlyCollection<IRaftClusterMember> members)
    {
        var mock = new Mock<IRaftCluster>();
        mock.Setup(c => c.LeadershipToken).Returns(CancellationToken.None);
        mock.Setup(c => c.Leader).Returns((IClusterMember?)null);
        mock.Setup(c => c.Members).Returns(members);
        return mock;
    }
}
