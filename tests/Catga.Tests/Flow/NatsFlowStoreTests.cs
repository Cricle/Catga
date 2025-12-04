using Catga.Flow;
using Catga.Persistence.Nats.Flow;
using FluentAssertions;
using NATS.Client.Core;

namespace Catga.Tests.Flow;

/// <summary>
/// NATS Flow Store tests. Requires NATS server with JetStream.
/// Skip if NATS is not available.
/// </summary>
[Collection("Nats")]
public class NatsFlowStoreTests : IAsyncLifetime
{
    private NatsConnection? _nats;
    private NatsFlowStore? _store;
    private readonly string _streamName = $"TESTFLOWS_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            _nats = new NatsConnection(new NatsOpts
            {
                Url = "nats://localhost:4222",
                ConnectTimeout = TimeSpan.FromSeconds(1)
            });
            await _nats.ConnectAsync();
            if (_nats.ConnectionState == NatsConnectionState.Open)
            {
                _store = new NatsFlowStore(_nats, _streamName);
            }
        }
        catch
        {
            // NATS not available
        }
    }

    public async Task DisposeAsync()
    {
        if (_nats != null)
        {
            await _nats.DisposeAsync();
        }
    }

    private void SkipIfNoNats()
    {
        Skip.If(_store == null, "NATS not available");
    }

    #region Basic Operations

    [SkippableFact]
    public async Task CreateAsync_NewFlow_ReturnsTrue()
    {
        SkipIfNoNats();

        var state = CreateState("flow-1");
        var result = await _store!.CreateAsync(state);

        result.Should().BeTrue();
    }

    [SkippableFact]
    public async Task CreateAsync_DuplicateId_ReturnsFalse()
    {
        SkipIfNoNats();

        var state1 = CreateState("flow-dup");
        var state2 = CreateState("flow-dup");

        await _store!.CreateAsync(state1);
        var result = await _store.CreateAsync(state2);

        result.Should().BeFalse();
    }

    [SkippableFact]
    public async Task GetAsync_ExistingFlow_ReturnsState()
    {
        SkipIfNoNats();

        var state = CreateState("flow-get");
        state.Step = 5;
        state.Data = new byte[] { 1, 2, 3, 4, 5 };
        await _store!.CreateAsync(state);

        var result = await _store.GetAsync("flow-get");

        result.Should().NotBeNull();
        result!.Id.Should().Be("flow-get");
        result.Type.Should().Be("TestFlow");
        result.Step.Should().Be(5);
        result.Data.Should().BeEquivalentTo(new byte[] { 1, 2, 3, 4, 5 });
    }

    [SkippableFact]
    public async Task GetAsync_NonExisting_ReturnsNull()
    {
        SkipIfNoNats();

        var result = await _store!.GetAsync("non-existing");

        result.Should().BeNull();
    }

    [SkippableFact]
    public async Task UpdateAsync_CorrectVersion_ReturnsTrue()
    {
        SkipIfNoNats();

        var state = CreateState("flow-update");
        await _store!.CreateAsync(state);

        state.Step = 10;
        state.Status = FlowStatus.Done;
        var result = await _store.UpdateAsync(state);

        result.Should().BeTrue();
        state.Version.Should().Be(1);

        var stored = await _store.GetAsync("flow-update");
        stored!.Step.Should().Be(10);
        stored.Status.Should().Be(FlowStatus.Done);
    }

    [SkippableFact]
    public async Task UpdateAsync_UpdatesData()
    {
        SkipIfNoNats();

        var state = CreateState("flow-data");
        state.Data = new byte[] { 1, 2, 3 };
        await _store!.CreateAsync(state);

        state.Data = new byte[] { 4, 5, 6, 7, 8 };
        await _store.UpdateAsync(state);

        var stored = await _store.GetAsync("flow-data");
        stored!.Data.Should().BeEquivalentTo(new byte[] { 4, 5, 6, 7, 8 });
    }

    #endregion

    #region Claim and Heartbeat

    [SkippableFact]
    public async Task TryClaimAsync_AbandonedFlow_ClaimsSuccessfully()
    {
        SkipIfNoNats();

        var state = CreateState("flow-claim");
        state.Owner = "old-node";
        state.HeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();
        await _store!.CreateAsync(state);

        var claimed = await _store.TryClaimAsync("TestFlow", "new-node", 60000);

        claimed.Should().NotBeNull();
        claimed!.Owner.Should().Be("new-node");
    }

    [SkippableFact]
    public async Task TryClaimAsync_ActiveFlow_ReturnsNull()
    {
        SkipIfNoNats();

        var state = CreateState("flow-active");
        state.Owner = "node-1";
        state.HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _store!.CreateAsync(state);

        var claimed = await _store.TryClaimAsync("TestFlow", "node-2", 60000);

        claimed.Should().BeNull();
    }

    [SkippableFact]
    public async Task TryClaimAsync_CompletedFlow_ReturnsNull()
    {
        SkipIfNoNats();

        var state = CreateState("flow-done");
        state.Status = FlowStatus.Done;
        state.HeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();
        await _store!.CreateAsync(state);

        var claimed = await _store.TryClaimAsync("TestFlow", "node-2", 60000);

        claimed.Should().BeNull();
    }

    [SkippableFact]
    public async Task HeartbeatAsync_CorrectOwnerAndVersion_ReturnsTrue()
    {
        SkipIfNoNats();

        var state = CreateState("flow-hb");
        state.Owner = "node-1";
        await _store!.CreateAsync(state);

        var result = await _store.HeartbeatAsync("flow-hb", "node-1", 0);

        result.Should().BeTrue();
    }

    [SkippableFact]
    public async Task HeartbeatAsync_WrongOwner_ReturnsFalse()
    {
        SkipIfNoNats();

        var state = CreateState("flow-hb-owner");
        state.Owner = "node-1";
        await _store!.CreateAsync(state);

        var result = await _store.HeartbeatAsync("flow-hb-owner", "node-2", 0);

        result.Should().BeFalse();
    }

    #endregion

    #region E2E Tests

    [SkippableFact]
    public async Task E2E_FullFlowLifecycle()
    {
        SkipIfNoNats();

        var executor = new FlowExecutor(_store!, new FlowOptions
        {
            NodeId = "test-node",
            HeartbeatInterval = TimeSpan.FromMilliseconds(100),
            ClaimTimeout = TimeSpan.FromSeconds(1)
        });

        var steps = new List<string>();
        var result = await executor.ExecuteAsync(
            "e2e-flow",
            "TestFlow",
            new byte[] { 1, 2, 3 },
            async (state, ct) =>
            {
                var flow = Catga.Flow.Flow.Create("E2E")
                    .Step(async c => { steps.Add("Step1"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("Step2"); await Task.Delay(10, c); })
                    .Step(async c => { steps.Add("Step3"); await Task.Delay(10, c); });

                return await flow.ExecuteAsync(ct);
            });

        result.IsSuccess.Should().BeTrue();
        steps.Should().BeEquivalentTo(["Step1", "Step2", "Step3"]);

        var stored = await _store!.GetAsync("e2e-flow");
        stored!.Status.Should().Be(FlowStatus.Done);
    }

    [SkippableFact]
    public async Task E2E_FlowWithCompensation()
    {
        SkipIfNoNats();

        var executor = new FlowExecutor(_store!);
        var compensated = new List<string>();

        var result = await executor.ExecuteAsync(
            "e2e-comp",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                var flow = Catga.Flow.Flow.Create("Compensation")
                    .Step(async c => { await Task.Delay(10, c); },
                        async c => { compensated.Add("Comp1"); await Task.Delay(10, c); })
                    .Step(async c => { await Task.Delay(10, c); },
                        async c => { compensated.Add("Comp2"); await Task.Delay(10, c); })
                    .Step(async c => { throw new Exception("fail"); });

                return await flow.ExecuteAsync(ct);
            });

        result.IsSuccess.Should().BeFalse();
        compensated.Should().Contain("Comp2");
        compensated.Should().Contain("Comp1");

        var stored = await _store!.GetAsync("e2e-comp");
        stored!.Status.Should().Be(FlowStatus.Failed);
    }

    [SkippableFact]
    public async Task E2E_ConcurrentFlows()
    {
        SkipIfNoNats();

        var executor = new FlowExecutor(_store!);
        var completedCount = 0;

        var tasks = Enumerable.Range(1, 10).Select(async i =>
        {
            var result = await executor.ExecuteAsync(
                $"concurrent-{i}",
                "TestFlow",
                ReadOnlyMemory<byte>.Empty,
                async (state, ct) =>
                {
                    await Task.Delay(50, ct);
                    Interlocked.Increment(ref completedCount);
                    return new FlowResult(true, 1, TimeSpan.Zero);
                });
            return result;
        }).ToList();

        var results = await Task.WhenAll(tasks);

        results.All(r => r.IsSuccess).Should().BeTrue();
        completedCount.Should().Be(10);
    }

    [SkippableFact]
    public async Task E2E_FlowRecovery()
    {
        SkipIfNoNats();

        // Create abandoned flow
        var state = CreateState("recovery-flow");
        state.Step = 2;
        state.Owner = "dead-node";
        state.HeartbeatAt = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds();
        await _store!.CreateAsync(state);

        // New node recovers
        var executor = new FlowExecutor(_store, new FlowOptions
        {
            NodeId = "recovery-node",
            ClaimTimeout = TimeSpan.FromSeconds(1)
        });

        var executed = false;
        var result = await executor.ExecuteAsync(
            "recovery-flow",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (s, ct) =>
            {
                executed = true;
                s.Step.Should().Be(2);
                return new FlowResult(true, 3, TimeSpan.Zero);
            });

        result.IsSuccess.Should().BeTrue();
        executed.Should().BeTrue();
    }

    #endregion

    private static FlowState CreateState(string id) => new()
    {
        Id = id,
        Type = "TestFlow",
        Status = FlowStatus.Running,
        Step = 0,
        Version = 0,
        Owner = null,
        HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    };
}
