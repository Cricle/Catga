using Catga.Abstractions;
using Catga.Persistence.Redis.Locking;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Catga.Tests.Distributed;

public class LeaderElectionTests : IAsyncLifetime
{
    private RedisContainer? _container;
    private IConnectionMultiplexer? _redis;

    public async Task InitializeAsync()
    {
        if (!IsDockerAvailable())
        {
            return;
        }

        _container = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        await _container.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (_redis != null)
            await _redis.CloseAsync();
        if (_container != null)
            await _container.DisposeAsync();
    }

    private static bool IsDockerAvailable()
    {
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    [Fact]
    public async Task TryAcquireLeadership_FirstNode_Succeeds()
    {
        if (_redis is null) return;

        var options = Options.Create(new LeaderElectionOptions
        {
            NodeId = "node-1",
            LeaseDuration = TimeSpan.FromSeconds(10)
        });

        using var election = new RedisLeaderElection(_redis, options, NullLogger<RedisLeaderElection>.Instance);

        var handle = await election.TryAcquireLeadershipAsync("test-election");

        handle.Should().NotBeNull();
        handle!.IsLeader.Should().BeTrue();
        handle.NodeId.Should().Be("node-1");

        await handle.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireLeadership_SecondNode_Fails()
    {
        if (_redis is null) return;

        var electionId = "contested-election-" + Guid.NewGuid().ToString("N");

        var options1 = Options.Create(new LeaderElectionOptions
        {
            NodeId = "node-1",
            LeaseDuration = TimeSpan.FromSeconds(30)
        });

        var options2 = Options.Create(new LeaderElectionOptions
        {
            NodeId = "node-2",
            LeaseDuration = TimeSpan.FromSeconds(30)
        });

        using var election1 = new RedisLeaderElection(_redis, options1, NullLogger<RedisLeaderElection>.Instance);
        using var election2 = new RedisLeaderElection(_redis, options2, NullLogger<RedisLeaderElection>.Instance);

        // Node 1 acquires leadership
        var handle1 = await election1.TryAcquireLeadershipAsync(electionId);
        handle1.Should().NotBeNull();

        // Node 2 should fail
        var handle2 = await election2.TryAcquireLeadershipAsync(electionId);
        handle2.Should().BeNull();

        await handle1!.DisposeAsync();
    }

    [Fact]
    public async Task IsLeader_ReturnsCorrectState()
    {
        if (_redis is null) return;

        var electionId = "isleader-test-" + Guid.NewGuid().ToString("N");

        var options = Options.Create(new LeaderElectionOptions
        {
            NodeId = "node-1",
            LeaseDuration = TimeSpan.FromSeconds(10)
        });

        using var election = new RedisLeaderElection(_redis, options, NullLogger<RedisLeaderElection>.Instance);

        // Before acquiring
        var isLeaderBefore = await election.IsLeaderAsync(electionId);
        isLeaderBefore.Should().BeFalse();

        // After acquiring
        var handle = await election.TryAcquireLeadershipAsync(electionId);
        var isLeaderAfter = await election.IsLeaderAsync(electionId);
        isLeaderAfter.Should().BeTrue();

        // After resigning
        await handle!.DisposeAsync();
        var isLeaderAfterResign = await election.IsLeaderAsync(electionId);
        isLeaderAfterResign.Should().BeFalse();
    }

    [Fact]
    public async Task GetLeader_ReturnsLeaderInfo()
    {
        if (_redis is null) return;

        var electionId = "getleader-test-" + Guid.NewGuid().ToString("N");

        var options = Options.Create(new LeaderElectionOptions
        {
            NodeId = "node-leader",
            LeaseDuration = TimeSpan.FromSeconds(10),
            Endpoint = "http://localhost:5000"
        });

        using var election = new RedisLeaderElection(_redis, options, NullLogger<RedisLeaderElection>.Instance);

        var handle = await election.TryAcquireLeadershipAsync(electionId);

        var leaderInfo = await election.GetLeaderAsync(electionId);

        leaderInfo.Should().NotBeNull();
        leaderInfo!.Value.NodeId.Should().Be("node-leader");
        leaderInfo.Value.Endpoint.Should().Be("http://localhost:5000");

        await handle!.DisposeAsync();
    }

    [Fact]
    public async Task AcquireLeadership_WaitsForLeader()
    {
        if (_redis is null) return;

        var electionId = "wait-election-" + Guid.NewGuid().ToString("N");

        var options1 = Options.Create(new LeaderElectionOptions
        {
            NodeId = "node-1",
            LeaseDuration = TimeSpan.FromSeconds(2)
        });

        var options2 = Options.Create(new LeaderElectionOptions
        {
            NodeId = "node-2",
            LeaseDuration = TimeSpan.FromSeconds(10)
        });

        using var election1 = new RedisLeaderElection(_redis, options1, NullLogger<RedisLeaderElection>.Instance);
        using var election2 = new RedisLeaderElection(_redis, options2, NullLogger<RedisLeaderElection>.Instance);

        // Node 1 acquires leadership
        var handle1 = await election1.TryAcquireLeadershipAsync(electionId);
        handle1.Should().NotBeNull();

        // Node 2 waits - should succeed after node 1's lease expires
        var waitTask = election2.AcquireLeadershipAsync(electionId, TimeSpan.FromSeconds(5));

        // Resign node 1
        await Task.Delay(500);
        await handle1!.DisposeAsync();

        var handle2 = await waitTask;
        handle2.Should().NotBeNull();
        handle2.NodeId.Should().Be("node-2");

        await handle2.DisposeAsync();
    }

    [Fact]
    public async Task Leadership_ExpiresAutomatically()
    {
        if (_redis is null) return;

        var electionId = "expire-test-" + Guid.NewGuid().ToString("N");

        var options = Options.Create(new LeaderElectionOptions
        {
            NodeId = "node-1",
            LeaseDuration = TimeSpan.FromSeconds(1),
            RenewInterval = TimeSpan.FromHours(1) // Disable auto-renew
        });

        using var election = new RedisLeaderElection(_redis, options, NullLogger<RedisLeaderElection>.Instance);

        var handle = await election.TryAcquireLeadershipAsync(electionId);
        handle.Should().NotBeNull();

        // Wait for lease to expire
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        // Leadership should be gone
        var isLeader = await election.IsLeaderAsync(electionId);
        isLeader.Should().BeFalse();
    }
}
