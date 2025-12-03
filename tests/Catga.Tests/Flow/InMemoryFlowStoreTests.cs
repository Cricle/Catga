using Catga.Flow;
using Catga.Persistence.InMemory.Flow;
using FluentAssertions;

namespace Catga.Tests.Flow;

public class InMemoryFlowStoreTests
{
    private readonly InMemoryFlowStore _store = new();

    [Fact]
    public async Task CreateAsync_NewFlow_ReturnsTrue()
    {
        var state = CreateState("flow-1");

        var result = await _store.CreateAsync(state);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_DuplicateId_ReturnsFalse()
    {
        var state1 = CreateState("flow-1");
        var state2 = CreateState("flow-1");

        await _store.CreateAsync(state1);
        var result = await _store.CreateAsync(state2);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_ExistingFlow_ReturnsState()
    {
        var state = CreateState("flow-1");
        await _store.CreateAsync(state);

        var result = await _store.GetAsync("flow-1");

        result.Should().NotBeNull();
        result!.Id.Should().Be("flow-1");
        result.Type.Should().Be("TestFlow");
    }

    [Fact]
    public async Task GetAsync_NonExisting_ReturnsNull()
    {
        var result = await _store.GetAsync("non-existing");

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_CorrectVersion_ReturnsTrue()
    {
        var state = CreateState("flow-1");
        await _store.CreateAsync(state);

        state.Step = 2;
        state.Status = FlowStatus.Running;
        var result = await _store.UpdateAsync(state);

        result.Should().BeTrue();
        state.Version.Should().Be(1);
    }

    [Fact]
    public async Task UpdateAsync_WrongVersion_ReturnsFalse()
    {
        var state = CreateState("flow-1");
        await _store.CreateAsync(state);

        // Get state and capture version
        var state1 = await _store.GetAsync("flow-1");
        var capturedVersion = state1!.Version;

        // First update succeeds
        state1.Step = 1;
        var result1 = await _store.UpdateAsync(state1);
        result1.Should().BeTrue();

        // Create a stale state with old version
        var staleState = new FlowState
        {
            Id = "flow-1",
            Type = "TestFlow",
            Status = FlowStatus.Running,
            Step = 2,
            Version = capturedVersion // Old version
        };
        var result2 = await _store.UpdateAsync(staleState);
        result2.Should().BeFalse();
    }

    [Fact]
    public async Task TryClaimAsync_AbandonedFlow_ClaimsSuccessfully()
    {
        var state = CreateState("flow-1");
        state.HeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();
        state.Owner = "node-1";
        await _store.CreateAsync(state);

        var claimed = await _store.TryClaimAsync("TestFlow", "node-2", 60000);

        claimed.Should().NotBeNull();
        claimed!.Owner.Should().Be("node-2");
    }

    [Fact]
    public async Task TryClaimAsync_ActiveFlow_ReturnsNull()
    {
        var state = CreateState("flow-1");
        state.HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        state.Owner = "node-1";
        await _store.CreateAsync(state);

        var claimed = await _store.TryClaimAsync("TestFlow", "node-2", 60000);

        claimed.Should().BeNull();
    }

    [Fact]
    public async Task TryClaimAsync_CompletedFlow_ReturnsNull()
    {
        var state = CreateState("flow-1");
        state.Status = FlowStatus.Done;
        state.HeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();
        await _store.CreateAsync(state);

        var claimed = await _store.TryClaimAsync("TestFlow", "node-2", 60000);

        claimed.Should().BeNull();
    }

    [Fact]
    public async Task HeartbeatAsync_CorrectOwnerAndVersion_ReturnsTrue()
    {
        var state = CreateState("flow-1");
        state.Owner = "node-1";
        await _store.CreateAsync(state);

        var result = await _store.HeartbeatAsync("flow-1", "node-1", 0);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HeartbeatAsync_WrongOwner_ReturnsFalse()
    {
        var state = CreateState("flow-1");
        state.Owner = "node-1";
        await _store.CreateAsync(state);

        var result = await _store.HeartbeatAsync("flow-1", "node-2", 0);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HeartbeatAsync_WrongVersion_ReturnsFalse()
    {
        var state = CreateState("flow-1");
        state.Owner = "node-1";
        await _store.CreateAsync(state);

        var result = await _store.HeartbeatAsync("flow-1", "node-1", 999);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentClaims_OnlyOneSucceeds()
    {
        var state = CreateState("flow-1");
        state.HeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();
        await _store.CreateAsync(state);

        var tasks = Enumerable.Range(1, 10)
            .Select(async i => await _store.TryClaimAsync("TestFlow", $"node-{i}", 60000))
            .ToList();

        var results = await Task.WhenAll(tasks);
        var claimed = results.Where(r => r != null).ToList();

        claimed.Should().HaveCount(1);
    }

    [Fact]
    public async Task ConcurrentUpdates_OnlyOneSucceeds()
    {
        var state = CreateState("flow-1");
        await _store.CreateAsync(state);

        // Get initial version
        var initialState = await _store.GetAsync("flow-1");
        var initialVersion = initialState!.Version;

        // Create multiple update attempts with same version
        var tasks = Enumerable.Range(1, 10)
            .Select(async i =>
            {
                var s = new FlowState
                {
                    Id = "flow-1",
                    Type = "TestFlow",
                    Status = FlowStatus.Running,
                    Step = i,
                    Version = initialVersion // All use same version
                };
                return await _store.UpdateAsync(s);
            })
            .ToList();

        var results = await Task.WhenAll(tasks);
        var succeeded = results.Count(r => r);

        succeeded.Should().Be(1);
    }

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
