using Catga.Flow;
using Catga.Persistence.InMemory.Flow;
using FluentAssertions;

namespace Catga.Tests.Flow;

/// <summary>
/// Contract tests to verify IFlowStore implementations behave consistently.
/// These tests define the expected behavior that all implementations must follow.
/// </summary>
public class FlowStoreContractTests
{
    private readonly InMemoryFlowStore _store = new();

    #region CreateAsync Contract

    [Fact]
    public async Task CreateAsync_NewFlow_ReturnsTrue()
    {
        var state = CreateState("create-new");
        var result = await _store.CreateAsync(state);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_DuplicateId_ReturnsFalse()
    {
        var state1 = CreateState("duplicate-id");
        var state2 = CreateState("duplicate-id");

        await _store.CreateAsync(state1);
        var result = await _store.CreateAsync(state2);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_PreservesAllFields()
    {
        var state = new FlowState
        {
            Id = "preserve-fields",
            Type = "CustomType",
            Status = FlowStatus.Running,
            Step = 5,
            Version = 0,
            Owner = "owner-1",
            HeartbeatAt = 123456789L,
            Data = new byte[] { 1, 2, 3 },
            Error = "initial error"
        };

        await _store.CreateAsync(state);
        var stored = await _store.GetAsync("preserve-fields");

        stored.Should().NotBeNull();
        stored!.Id.Should().Be("preserve-fields");
        stored.Type.Should().Be("CustomType");
        stored.Status.Should().Be(FlowStatus.Running);
        stored.Step.Should().Be(5);
        stored.Owner.Should().Be("owner-1");
        stored.HeartbeatAt.Should().Be(123456789L);
        stored.Data.Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
        stored.Error.Should().Be("initial error");
    }

    #endregion

    #region GetAsync Contract

    [Fact]
    public async Task GetAsync_ExistingFlow_ReturnsState()
    {
        var state = CreateState("get-existing");
        await _store.CreateAsync(state);

        var result = await _store.GetAsync("get-existing");

        result.Should().NotBeNull();
        result!.Id.Should().Be("get-existing");
    }

    [Fact]
    public async Task GetAsync_NonExistingFlow_ReturnsNull()
    {
        var result = await _store.GetAsync("non-existing");
        result.Should().BeNull();
    }

    #endregion

    #region UpdateAsync Contract

    [Fact]
    public async Task UpdateAsync_CorrectVersion_ReturnsTrue()
    {
        var state = CreateState("update-correct");
        await _store.CreateAsync(state);

        state.Step = 1;
        var result = await _store.UpdateAsync(state);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_WrongVersion_ReturnsFalse()
    {
        var state = CreateState("update-wrong");
        await _store.CreateAsync(state);

        // Update once to increment version
        state.Step = 1;
        await _store.UpdateAsync(state);

        // Try update with old version
        var staleState = new FlowState
        {
            Id = "update-wrong",
            Type = "TestFlow",
            Status = FlowStatus.Running,
            Step = 2,
            Version = 0 // Stale version
        };
        var result = await _store.UpdateAsync(staleState);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_NonExisting_ReturnsFalse()
    {
        var state = CreateState("update-non-existing");
        var result = await _store.UpdateAsync(state);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_IncrementsVersion()
    {
        var state = CreateState("update-version");
        await _store.CreateAsync(state);

        var before = state.Version;
        await _store.UpdateAsync(state);
        var after = state.Version;

        after.Should().Be(before + 1);
    }

    [Fact]
    public async Task UpdateAsync_DoesNotModifyVersionOnFailure()
    {
        var state = CreateState("update-fail-version");
        state.Version = 999; // Non-existing flow

        var before = state.Version;
        await _store.UpdateAsync(state);
        var after = state.Version;

        after.Should().Be(before);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesAllFields()
    {
        var state = CreateState("update-all-fields");
        await _store.CreateAsync(state);

        state.Status = FlowStatus.Done;
        state.Step = 10;
        state.Owner = "new-owner";
        state.HeartbeatAt = 999999L;
        state.Data = new byte[] { 9, 8, 7 };
        state.Error = "new error";
        await _store.UpdateAsync(state);

        var stored = await _store.GetAsync("update-all-fields");
        stored!.Status.Should().Be(FlowStatus.Done);
        stored.Step.Should().Be(10);
        stored.Owner.Should().Be("new-owner");
        stored.HeartbeatAt.Should().Be(999999L);
        stored.Data.Should().BeEquivalentTo(new byte[] { 9, 8, 7 });
        stored.Error.Should().Be("new error");
    }

    #endregion

    #region TryClaimAsync Contract

    [Fact]
    public async Task TryClaimAsync_AbandonedFlow_ClaimsSuccessfully()
    {
        var state = CreateState("claim-abandoned");
        state.HeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();
        await _store.CreateAsync(state);

        var claimed = await _store.TryClaimAsync("TestFlow", "new-owner", 60000);

        claimed.Should().NotBeNull();
        claimed!.Owner.Should().Be("new-owner");
    }

    [Fact]
    public async Task TryClaimAsync_ActiveFlow_ReturnsNull()
    {
        var state = CreateState("claim-active");
        state.HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _store.CreateAsync(state);

        var claimed = await _store.TryClaimAsync("TestFlow", "new-owner", 60000);

        claimed.Should().BeNull();
    }

    [Fact]
    public async Task TryClaimAsync_CompletedFlow_ReturnsNull()
    {
        var state = CreateState("claim-completed");
        state.Status = FlowStatus.Done;
        state.HeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();
        await _store.CreateAsync(state);

        var claimed = await _store.TryClaimAsync("TestFlow", "new-owner", 60000);

        claimed.Should().BeNull();
    }

    [Fact]
    public async Task TryClaimAsync_FailedFlow_ReturnsNull()
    {
        var state = CreateState("claim-failed");
        state.Status = FlowStatus.Failed;
        state.HeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();
        await _store.CreateAsync(state);

        var claimed = await _store.TryClaimAsync("TestFlow", "new-owner", 60000);

        claimed.Should().BeNull();
    }

    [Fact]
    public async Task TryClaimAsync_WrongType_ReturnsNull()
    {
        var state = CreateState("claim-wrong-type");
        state.HeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();
        await _store.CreateAsync(state);

        var claimed = await _store.TryClaimAsync("OtherType", "new-owner", 60000);

        claimed.Should().BeNull();
    }

    [Fact]
    public async Task TryClaimAsync_UpdatesHeartbeat()
    {
        var oldHeartbeat = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();
        var state = CreateState("claim-heartbeat");
        state.HeartbeatAt = oldHeartbeat;
        await _store.CreateAsync(state);

        var claimed = await _store.TryClaimAsync("TestFlow", "new-owner", 60000);

        claimed!.HeartbeatAt.Should().BeGreaterThan(oldHeartbeat);
    }

    #endregion

    #region HeartbeatAsync Contract

    [Fact]
    public async Task HeartbeatAsync_CorrectOwnerAndVersion_ReturnsTrue()
    {
        var state = CreateState("heartbeat-correct");
        state.Owner = "owner-1";
        await _store.CreateAsync(state);

        var result = await _store.HeartbeatAsync("heartbeat-correct", "owner-1", 0);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HeartbeatAsync_WrongOwner_ReturnsFalse()
    {
        var state = CreateState("heartbeat-wrong-owner");
        state.Owner = "owner-1";
        await _store.CreateAsync(state);

        var result = await _store.HeartbeatAsync("heartbeat-wrong-owner", "owner-2", 0);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HeartbeatAsync_WrongVersion_ReturnsFalse()
    {
        var state = CreateState("heartbeat-wrong-version");
        state.Owner = "owner-1";
        await _store.CreateAsync(state);

        var result = await _store.HeartbeatAsync("heartbeat-wrong-version", "owner-1", 999);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HeartbeatAsync_NonExisting_ReturnsFalse()
    {
        var result = await _store.HeartbeatAsync("non-existing", "owner-1", 0);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task HeartbeatAsync_UpdatesHeartbeatTime()
    {
        var oldHeartbeat = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds();
        var state = CreateState("heartbeat-time");
        state.Owner = "owner-1";
        state.HeartbeatAt = oldHeartbeat;
        await _store.CreateAsync(state);

        await _store.HeartbeatAsync("heartbeat-time", "owner-1", 0);

        var stored = await _store.GetAsync("heartbeat-time");
        stored!.HeartbeatAt.Should().BeGreaterThan(oldHeartbeat);
    }

    [Fact]
    public async Task HeartbeatAsync_IncrementsVersion()
    {
        var state = CreateState("heartbeat-version");
        state.Owner = "owner-1";
        await _store.CreateAsync(state);

        await _store.HeartbeatAsync("heartbeat-version", "owner-1", 0);

        var stored = await _store.GetAsync("heartbeat-version");
        stored!.Version.Should().Be(1);
    }

    #endregion

    #region Concurrency Contract

    [Fact]
    public async Task Concurrency_ParallelCreates_OnlyOneSucceeds()
    {
        var tasks = Enumerable.Range(1, 10)
            .Select(_ => _store.CreateAsync(CreateState("parallel-create")).AsTask())
            .ToList();

        var results = await Task.WhenAll(tasks);
        results.Count(r => r).Should().Be(1);
    }

    [Fact]
    public async Task Concurrency_ParallelUpdates_OnlyOneSucceeds()
    {
        var state = CreateState("parallel-update");
        await _store.CreateAsync(state);

        var tasks = Enumerable.Range(1, 10)
            .Select(i =>
            {
                var s = new FlowState
                {
                    Id = "parallel-update",
                    Type = "TestFlow",
                    Status = FlowStatus.Running,
                    Step = i,
                    Version = 0
                };
                return _store.UpdateAsync(s).AsTask();
            })
            .ToList();

        var results = await Task.WhenAll(tasks);
        results.Count(r => r).Should().Be(1);
    }

    [Fact]
    public async Task Concurrency_ParallelClaims_OnlyOneSucceeds()
    {
        var state = CreateState("parallel-claim");
        state.HeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();
        await _store.CreateAsync(state);

        var tasks = Enumerable.Range(1, 10)
            .Select(i => _store.TryClaimAsync("TestFlow", $"owner-{i}", 60000).AsTask())
            .ToList();

        var results = await Task.WhenAll(tasks);
        results.Count(r => r != null).Should().Be(1);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task EdgeCase_EmptyData_HandledCorrectly()
    {
        var state = CreateState("empty-data");
        state.Data = Array.Empty<byte>();
        await _store.CreateAsync(state);

        var stored = await _store.GetAsync("empty-data");
        stored!.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task EdgeCase_NullData_HandledCorrectly()
    {
        var state = CreateState("null-data");
        state.Data = null;
        await _store.CreateAsync(state);

        var stored = await _store.GetAsync("null-data");
        stored!.Data.Should().BeNull();
    }

    [Fact]
    public async Task EdgeCase_EmptyOwner_HandledCorrectly()
    {
        var state = CreateState("empty-owner");
        state.Owner = "";
        await _store.CreateAsync(state);

        var stored = await _store.GetAsync("empty-owner");
        stored!.Owner.Should().BeEmpty();
    }

    [Fact]
    public async Task EdgeCase_EmptyError_HandledCorrectly()
    {
        var state = CreateState("empty-error");
        state.Error = "";
        await _store.CreateAsync(state);

        var stored = await _store.GetAsync("empty-error");
        stored!.Error.Should().BeEmpty();
    }

    [Fact]
    public async Task EdgeCase_LongFlowId_HandledCorrectly()
    {
        var longId = new string('x', 256);
        var state = CreateState(longId);
        await _store.CreateAsync(state);

        var stored = await _store.GetAsync(longId);
        stored.Should().NotBeNull();
        stored!.Id.Should().Be(longId);
    }

    [Fact]
    public async Task EdgeCase_SpecialCharactersInId_HandledCorrectly()
    {
        var specialId = "flow:test/path?query=1&other=2";
        var state = CreateState(specialId);
        await _store.CreateAsync(state);

        var stored = await _store.GetAsync(specialId);
        stored.Should().NotBeNull();
        stored!.Id.Should().Be(specialId);
    }

    [Fact]
    public async Task EdgeCase_MaxStep_HandledCorrectly()
    {
        var state = CreateState("max-step");
        state.Step = int.MaxValue;
        await _store.CreateAsync(state);

        var stored = await _store.GetAsync("max-step");
        stored!.Step.Should().Be(int.MaxValue);
    }

    [Fact]
    public async Task EdgeCase_MaxVersion_HandledCorrectly()
    {
        var state = CreateState("max-version");
        await _store.CreateAsync(state);

        // Update many times
        for (int i = 0; i < 100; i++)
        {
            var current = await _store.GetAsync("max-version");
            current!.Step = i;
            await _store.UpdateAsync(current);
        }

        var stored = await _store.GetAsync("max-version");
        stored!.Version.Should().Be(100);
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
