using Catga.Cluster;
using Catga.Cluster.Routing;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Cluster;

public class RouterTests
{
    private readonly List<ClusterNode> _nodes = new()
    {
        new ClusterNode { NodeId = "node-1", Endpoint = "http://node1:5000", Load = 10 },
        new ClusterNode { NodeId = "node-2", Endpoint = "http://node2:5000", Load = 50 },
        new ClusterNode { NodeId = "node-3", Endpoint = "http://node3:5000", Load = 80 }
    };

    [Fact]
    public async Task RoundRobinRouter_ShouldDistributeEvenly()
    {
        // Arrange
        var router = new RoundRobinRouter();
        var distribution = new Dictionary<string, int>();

        // Act
        for (var i = 0; i < 300; i++)
        {
            var node = await router.RouteAsync($"message-{i}", _nodes);
            distribution.TryAdd(node.NodeId, 0);
            distribution[node.NodeId]++;
        }

        // Assert - 每个节点应该分配到 100 次（轮询）
        distribution.Should().HaveCount(3);
        distribution["node-1"].Should().Be(100);
        distribution["node-2"].Should().Be(100);
        distribution["node-3"].Should().Be(100);
    }

    [Fact]
    public async Task WeightedRoundRobinRouter_ShouldFavorLowLoadNodes()
    {
        // Arrange
        var router = new WeightedRoundRobinRouter();
        var distribution = new Dictionary<string, int>();

        // Act
        for (var i = 0; i < 1000; i++)
        {
            var node = await router.RouteAsync($"message-{i}", _nodes);
            distribution.TryAdd(node.NodeId, 0);
            distribution[node.NodeId]++;
        }

        // Assert - 负载低的节点应该获得更多请求
        distribution.Should().HaveCount(3);
        distribution["node-1"].Should().BeGreaterThan(distribution["node-2"]);
        distribution["node-2"].Should().BeGreaterThan(distribution["node-3"]);
    }

    [Fact]
    public async Task ConsistentHashRouter_ShouldRouteSameMessageToSameNode()
    {
        // Arrange
        var router = new ConsistentHashRouter();

        // Act
        var node1 = await router.RouteAsync("test-message", _nodes);
        var node2 = await router.RouteAsync("test-message", _nodes);
        var node3 = await router.RouteAsync("test-message", _nodes);

        // Assert - 同样的消息应该路由到同一个节点
        node1.NodeId.Should().Be(node2.NodeId);
        node2.NodeId.Should().Be(node3.NodeId);
    }

    [Fact]
    public async Task ConsistentHashRouter_ShouldDistributeEvenly()
    {
        // Arrange
        var router = new ConsistentHashRouter(virtualNodeCount: 150);
        var distribution = new Dictionary<string, int>();

        // Act
        for (var i = 0; i < 10000; i++)
        {
            var node = await router.RouteAsync($"message-{i}", _nodes);
            distribution.TryAdd(node.NodeId, 0);
            distribution[node.NodeId]++;
        }

        // Assert - 分布应该相对均匀（每个节点约 33%，允许 10% 偏差）
        distribution.Should().HaveCount(3);
        var average = 10000 / 3;
        foreach (var count in distribution.Values)
        {
            count.Should().BeInRange((int)(average * 0.9), (int)(average * 1.1));
        }
    }

    [Fact]
    public async Task LeastConnectionsRouter_ShouldSelectNodeWithFewestConnections()
    {
        // Arrange
        var router = new LeastConnectionsRouter();

        // Act
        var node1 = await router.RouteAsync("message-1", _nodes);
        var node2 = await router.RouteAsync("message-2", _nodes);
        var node3 = await router.RouteAsync("message-3", _nodes);

        // Assert - 应该选择不同的节点（因为每次都选择连接数最少的）
        var nodeIds = new[] { node1.NodeId, node2.NodeId, node3.NodeId };
        nodeIds.Distinct().Should().HaveCount(3);
    }

    [Fact]
    public async Task LeastConnectionsRouter_ShouldRespectConnectionCounts()
    {
        // Arrange
        var router = new LeastConnectionsRouter();

        // Act - 第一次请求
        var node1 = await router.RouteAsync("message-1", _nodes);
        
        // 模拟请求完成
        router.DecrementConnections(node1.NodeId);
        
        // 第二次请求应该优先选择连接数为 0 的节点
        var node2 = await router.RouteAsync("message-2", _nodes);

        // Assert
        router.GetActiveConnections(node1.NodeId).Should().Be(0);
        router.GetActiveConnections(node2.NodeId).Should().Be(1);
    }

    [Fact]
    public async Task AllRouters_ShouldThrowWhenNoNodesAvailable()
    {
        // Arrange
        var emptyNodes = new List<ClusterNode>();
        var roundRobin = new RoundRobinRouter();
        var weighted = new WeightedRoundRobinRouter();
        var consistentHash = new ConsistentHashRouter();
        var leastConnections = new LeastConnectionsRouter();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => roundRobin.RouteAsync("message", emptyNodes));
        
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => weighted.RouteAsync("message", emptyNodes));
        
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => consistentHash.RouteAsync("message", emptyNodes));
        
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => leastConnections.RouteAsync("message", emptyNodes));
    }
}

