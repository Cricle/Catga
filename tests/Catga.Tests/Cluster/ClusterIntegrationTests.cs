using Catga.Abstractions;
using Catga.Cluster;
using Catga.Cluster.DependencyInjection;
using Catga.Core;
using Catga.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Cluster;

/// <summary>
/// Integration tests for Cluster functionality.
/// Tests DI registration and basic cluster coordination.
/// </summary>
public class ClusterIntegrationTests
{
    [Fact]
    public void AddCatgaCluster_ShouldRegisterClusterCoordinator()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockRaft = Substitute.For<DotNext.Net.Cluster.Consensus.Raft.IRaftCluster>();
        var cts = new CancellationTokenSource();
        mockRaft.LeadershipToken.Returns(cts.Token);
        services.AddSingleton(mockRaft);
        
        // Act
        services.AddCatgaCluster();
        var sp = services.BuildServiceProvider();
        
        // Assert
        var coordinator = sp.GetService<IClusterCoordinator>();
        coordinator.Should().NotBeNull();
        coordinator.Should().BeOfType<ClusterCoordinator>();
    }

    [Fact]
    public void AddLeaderOnlyBehavior_ShouldRegisterBehavior()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockRaft = Substitute.For<DotNext.Net.Cluster.Consensus.Raft.IRaftCluster>();
        services.AddSingleton(mockRaft);
        services.AddCatgaCluster();
        
        // Act
        services.AddLeaderOnlyBehavior<TestCommand, TestResponse>();
        var sp = services.BuildServiceProvider();
        
        // Assert
        var behaviors = sp.GetServices<Catga.Pipeline.IPipelineBehavior<TestCommand, TestResponse>>();
        behaviors.Should().Contain(b => b is LeaderOnlyBehavior<TestCommand, TestResponse>);
    }

    [Fact]
    public void AddForwardToLeaderBehavior_ShouldRegisterBehavior()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockRaft = Substitute.For<DotNext.Net.Cluster.Consensus.Raft.IRaftCluster>();
        services.AddSingleton(mockRaft);
        services.AddCatgaCluster();
        
        // Act
        services.AddForwardToLeaderBehavior<TestCommand, TestResponse>();
        var sp = services.BuildServiceProvider();
        
        // Assert
        var behaviors = sp.GetServices<Catga.Pipeline.IPipelineBehavior<TestCommand, TestResponse>>();
        behaviors.Should().Contain(b => b is ForwardToLeaderBehavior<TestCommand, TestResponse>);
    }

    [Fact]
    public void AddSingletonTask_ShouldRegisterHostedService()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockRaft = Substitute.For<DotNext.Net.Cluster.Consensus.Raft.IRaftCluster>();
        var cts = new CancellationTokenSource();
        mockRaft.LeadershipToken.Returns(cts.Token);
        services.AddSingleton(mockRaft);
        services.AddCatgaCluster();
        
        // Act
        services.AddSingletonTask<TestSingletonTask>();
        var sp = services.BuildServiceProvider();
        
        // Assert
        var hostedServices = sp.GetServices<IHostedService>();
        hostedServices.Should().Contain(s => s is TestSingletonTask);
    }

    [Fact]
    public async Task ClusterCoordinator_WithMockRaft_ShouldWork()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockRaft = Substitute.For<DotNext.Net.Cluster.Consensus.Raft.IRaftCluster>();
        var mockLeader = Substitute.For<DotNext.Net.Cluster.IClusterMember>();
        
        mockLeader.IsRemote.Returns(false);
        mockLeader.EndPoint.Returns(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 5000));
        mockRaft.Leader.Returns(mockLeader);
        var cts = new CancellationTokenSource();
        mockRaft.LeadershipToken.Returns(cts.Token);
        
        services.AddSingleton(mockRaft);
        services.AddCatgaCluster();
        
        var sp = services.BuildServiceProvider();
        var coordinator = sp.GetRequiredService<IClusterCoordinator>();
        
        // Act & Assert
        coordinator.IsLeader.Should().BeTrue();
        coordinator.LeaderEndpoint.Should().NotBeNull();
        coordinator.NodeId.Should().NotBeNullOrEmpty();
        
        var executed = false;
        var success = await coordinator.ExecuteIfLeaderAsync(async ct =>
        {
            await Task.Delay(10, ct);
            executed = true;
        });
        
        success.Should().BeTrue();
        executed.Should().BeTrue();
    }

    // Test types
    public record TestCommand : IRequest<TestResponse>
    {
        public long MessageId { get; init; } = DateTimeOffset.UtcNow.Ticks;
    }

    public record TestResponse
    {
        public string Value { get; init; } = string.Empty;
    }

    public class TestSingletonTask : SingletonTaskRunner
    {
        public TestSingletonTask(IClusterCoordinator coordinator)
            : base(coordinator, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
        {
        }

        protected override string TaskName => "TestTask";

        protected override async Task ExecuteLeaderTaskAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(100, stoppingToken);
        }
    }
}
