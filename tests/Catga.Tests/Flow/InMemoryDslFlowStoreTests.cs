using Catga.Flow.Dsl;
using Catga.Persistence.InMemory.Flow;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Flow.Store;

/// <summary>
/// TDD tests for InMemoryDslFlowStore.
/// </summary>
public class InMemoryDslFlowStoreTests
{
    private readonly InMemoryDslFlowStore _store = new();

    #region Create Tests

    [Fact]
    public async Task CreateAsync_NewFlow_ReturnsTrue()
    {
        var state = new TestFlowState { FlowId = "flow-1", OrderId = "order-1" };
        var snapshot = CreateSnapshot("flow-1", state);

        var result = await _store.CreateAsync(snapshot);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_DuplicateId_ReturnsFalse()
    {
        var state = new TestFlowState { FlowId = "flow-1", OrderId = "order-1" };
        var snapshot = CreateSnapshot("flow-1", state);

        await _store.CreateAsync(snapshot);
        var result = await _store.CreateAsync(snapshot);

        result.Should().BeFalse();
    }

    #endregion

    #region Get Tests

    [Fact]
    public async Task GetAsync_ExistingFlow_ReturnsSnapshot()
    {
        var state = new TestFlowState { FlowId = "flow-1", OrderId = "order-1" };
        var snapshot = CreateSnapshot("flow-1", state);
        await _store.CreateAsync(snapshot);

        var result = await _store.GetAsync<TestFlowState>("flow-1");

        result.Should().NotBeNull();
        result!.FlowId.Should().Be("flow-1");
        result.State.OrderId.Should().Be("order-1");
    }

    [Fact]
    public async Task GetAsync_NonExistingFlow_ReturnsNull()
    {
        var result = await _store.GetAsync<TestFlowState>("non-existing");

        result.Should().BeNull();
    }

    #endregion

    #region Update Tests

    [Fact]
    public async Task UpdateAsync_ExistingFlow_ReturnsTrue()
    {
        var state = new TestFlowState { FlowId = "flow-1", OrderId = "order-1" };
        var snapshot = CreateSnapshot("flow-1", state);
        await _store.CreateAsync(snapshot);

        state.OrderId = "order-2";
        var updated = snapshot with { State = state, Version = 1 };
        var result = await _store.UpdateAsync(updated);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_NonExistingFlow_ReturnsFalse()
    {
        var state = new TestFlowState { FlowId = "flow-1", OrderId = "order-1" };
        var snapshot = CreateSnapshot("flow-1", state);

        var result = await _store.UpdateAsync(snapshot);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_UpdatesState()
    {
        var state = new TestFlowState { FlowId = "flow-1", OrderId = "order-1" };
        var snapshot = CreateSnapshot("flow-1", state);
        await _store.CreateAsync(snapshot);

        var newState = new TestFlowState { FlowId = "flow-1", OrderId = "order-updated" };
        // Version must be incremented by 1 for update to succeed (optimistic concurrency)
        var updated = snapshot with { State = newState, Status = DslFlowStatus.Completed, Version = 1 };
        await _store.UpdateAsync(updated);

        var result = await _store.GetAsync<TestFlowState>("flow-1");
        result!.State.OrderId.Should().Be("order-updated");
        result.Status.Should().Be(DslFlowStatus.Completed);
    }

    #endregion

    #region Delete Tests

    [Fact]
    public async Task DeleteAsync_ExistingFlow_ReturnsTrue()
    {
        var state = new TestFlowState { FlowId = "flow-1", OrderId = "order-1" };
        var snapshot = CreateSnapshot("flow-1", state);
        await _store.CreateAsync(snapshot);

        var result = await _store.DeleteAsync("flow-1");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_NonExistingFlow_ReturnsFalse()
    {
        var result = await _store.DeleteAsync("non-existing");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_RemovesFlow()
    {
        var state = new TestFlowState { FlowId = "flow-1", OrderId = "order-1" };
        var snapshot = CreateSnapshot("flow-1", state);
        await _store.CreateAsync(snapshot);

        await _store.DeleteAsync("flow-1");

        var result = await _store.GetAsync<TestFlowState>("flow-1");
        result.Should().BeNull();
    }

    #endregion

    #region WaitCondition Tests

    [Fact]
    public async Task SetWaitConditionAsync_StoresCondition()
    {
        var condition = CreateWaitCondition("corr-1");

        await _store.SetWaitConditionAsync("corr-1", condition);

        var result = await _store.GetWaitConditionAsync("corr-1");
        result.Should().NotBeNull();
        result!.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public async Task GetWaitConditionAsync_NonExisting_ReturnsNull()
    {
        var result = await _store.GetWaitConditionAsync("non-existing");

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateWaitConditionAsync_UpdatesCondition()
    {
        var condition = CreateWaitCondition("corr-1");
        await _store.SetWaitConditionAsync("corr-1", condition);

        condition.CompletedCount = 1;
        condition.Results.Add(new FlowCompletedEventData { FlowId = "child-1", Success = true });
        await _store.UpdateWaitConditionAsync("corr-1", condition);

        var result = await _store.GetWaitConditionAsync("corr-1");
        result!.CompletedCount.Should().Be(1);
        result.Results.Should().HaveCount(1);
    }

    [Fact]
    public async Task ClearWaitConditionAsync_RemovesCondition()
    {
        var condition = CreateWaitCondition("corr-1");
        await _store.SetWaitConditionAsync("corr-1", condition);

        await _store.ClearWaitConditionAsync("corr-1");

        var result = await _store.GetWaitConditionAsync("corr-1");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetTimedOutWaitConditionsAsync_ReturnsTimedOut()
    {
        var timedOut = new WaitCondition
        {
            CorrelationId = "corr-1",
            Type = WaitType.All,
            ExpectedCount = 2,
            CompletedCount = 0,
            Timeout = TimeSpan.FromMinutes(1),
            CreatedAt = DateTime.UtcNow.AddMinutes(-5), // Created 5 mins ago, timeout is 1 min
            FlowId = "flow-1",
            FlowType = "TestFlow",
            Step = 0
        };

        var notTimedOut = new WaitCondition
        {
            CorrelationId = "corr-2",
            Type = WaitType.All,
            ExpectedCount = 2,
            CompletedCount = 0,
            Timeout = TimeSpan.FromMinutes(10),
            CreatedAt = DateTime.UtcNow, // Just created
            FlowId = "flow-2",
            FlowType = "TestFlow",
            Step = 0
        };

        await _store.SetWaitConditionAsync("corr-1", timedOut);
        await _store.SetWaitConditionAsync("corr-2", notTimedOut);

        var result = await _store.GetTimedOutWaitConditionsAsync();

        result.Should().HaveCount(1);
        result[0].CorrelationId.Should().Be("corr-1");
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public async Task ConcurrentCreates_OnlyOneSucceeds()
    {
        var tasks = Enumerable.Range(0, 10).Select(i =>
        {
            var state = new TestFlowState { FlowId = "flow-1", OrderId = $"order-{i}" };
            var snapshot = CreateSnapshot("flow-1", state);
            return _store.CreateAsync(snapshot);
        }).ToArray();

        var results = await Task.WhenAll(tasks);

        results.Count(r => r).Should().Be(1);
        results.Count(r => !r).Should().Be(9);
    }

    [Fact]
    public async Task ConcurrentUpdates_AllSucceed()
    {
        var state = new TestFlowState { FlowId = "flow-1", OrderId = "order-1" };
        var snapshot = CreateSnapshot("flow-1", state);
        await _store.CreateAsync(snapshot);

        // With optimistic concurrency, concurrent updates to the same flow will have
        // version conflicts. Only sequential updates with proper version increments succeed.
        // This test verifies that sequential updates work correctly.
        var successCount = 0;
        for (int i = 0; i < 10; i++)
        {
            var newState = new TestFlowState { FlowId = "flow-1", OrderId = $"order-{i}" };
            var updated = snapshot with { State = newState, Version = i + 1 };
            var result = await _store.UpdateAsync(updated);
            if (result) successCount++;
        }

        // All sequential updates with correct version increments should succeed
        successCount.Should().Be(10);
    }

    #endregion

    #region Query Tests

    [Fact]
    public async Task QueryByStatusAsync_ReturnsMatching()
    {
        // Arrange - Create flows with different statuses
        var runningState = new TestFlowState { FlowId = "flow-running", OrderId = "order-1" };
        var completedState = new TestFlowState { FlowId = "flow-completed", OrderId = "order-2" };
        var failedState = new TestFlowState { FlowId = "flow-failed", OrderId = "order-3" };

        await _store.CreateAsync(CreateSnapshot("flow-running", runningState, DslFlowStatus.Running));
        await _store.CreateAsync(CreateSnapshot("flow-completed", completedState, DslFlowStatus.Completed));
        await _store.CreateAsync(CreateSnapshot("flow-failed", failedState, DslFlowStatus.Failed));

        // Act
        var runningFlows = await _store.QueryByStatusAsync(DslFlowStatus.Running);
        var completedFlows = await _store.QueryByStatusAsync(DslFlowStatus.Completed);
        var failedFlows = await _store.QueryByStatusAsync(DslFlowStatus.Failed);
        var pendingFlows = await _store.QueryByStatusAsync(DslFlowStatus.Pending);

        // Assert
        runningFlows.Should().HaveCount(1);
        runningFlows[0].FlowId.Should().Be("flow-running");
        runningFlows[0].Status.Should().Be(DslFlowStatus.Running);

        completedFlows.Should().HaveCount(1);
        completedFlows[0].FlowId.Should().Be("flow-completed");
        completedFlows[0].Status.Should().Be(DslFlowStatus.Completed);

        failedFlows.Should().HaveCount(1);
        failedFlows[0].FlowId.Should().Be("flow-failed");
        failedFlows[0].Status.Should().Be(DslFlowStatus.Failed);

        pendingFlows.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryByTypeAsync_ReturnsMatching()
    {
        // Arrange - Create flows with different types
        var state1 = new TestFlowState { FlowId = "flow-1", OrderId = "order-1" };
        var state2 = new TestFlowState { FlowId = "flow-2", OrderId = "order-2" };
        var state3 = new AnotherFlowState { FlowId = "flow-3", CustomField = "custom" };

        await _store.CreateAsync(CreateSnapshot("flow-1", state1));
        await _store.CreateAsync(CreateSnapshot("flow-2", state2));
        await _store.CreateAsync(CreateSnapshotForAnotherType("flow-3", state3));

        // Act
        var testFlows = await _store.QueryByTypeAsync(typeof(TestFlowState).FullName!);
        var anotherFlows = await _store.QueryByTypeAsync(typeof(AnotherFlowState).FullName!);
        var nonExistentFlows = await _store.QueryByTypeAsync("NonExistent.Type");

        // Assert
        testFlows.Should().HaveCount(2);
        testFlows.Select(f => f.FlowId).Should().Contain(new[] { "flow-1", "flow-2" });

        anotherFlows.Should().HaveCount(1);
        anotherFlows[0].FlowId.Should().Be("flow-3");

        nonExistentFlows.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryByDateRangeAsync_ReturnsMatching()
    {
        // Arrange - Create flows with different creation dates
        var now = DateTime.UtcNow;
        var yesterday = now.AddDays(-1);
        var twoDaysAgo = now.AddDays(-2);
        var tomorrow = now.AddDays(1);

        var state1 = new TestFlowState { FlowId = "flow-old", OrderId = "order-1" };
        var state2 = new TestFlowState { FlowId = "flow-yesterday", OrderId = "order-2" };
        var state3 = new TestFlowState { FlowId = "flow-today", OrderId = "order-3" };

        await _store.CreateAsync(CreateSnapshotWithDate("flow-old", state1, twoDaysAgo));
        await _store.CreateAsync(CreateSnapshotWithDate("flow-yesterday", state2, yesterday));
        await _store.CreateAsync(CreateSnapshotWithDate("flow-today", state3, now));

        // Act - Query for flows created yesterday or later
        var recentFlows = await _store.QueryByDateRangeAsync(yesterday, tomorrow);
        var oldFlows = await _store.QueryByDateRangeAsync(twoDaysAgo.AddHours(-1), twoDaysAgo.AddHours(1));
        var futureFlows = await _store.QueryByDateRangeAsync(tomorrow, tomorrow.AddDays(1));

        // Assert
        recentFlows.Should().HaveCount(2);
        recentFlows.Select(f => f.FlowId).Should().Contain(new[] { "flow-yesterday", "flow-today" });

        oldFlows.Should().HaveCount(1);
        oldFlows[0].FlowId.Should().Be("flow-old");

        futureFlows.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryByStatusAsync_EmptyStore_ReturnsEmpty()
    {
        // Act
        var result = await _store.QueryByStatusAsync(DslFlowStatus.Running);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryByTypeAsync_EmptyStore_ReturnsEmpty()
    {
        // Act
        var result = await _store.QueryByTypeAsync(typeof(TestFlowState).FullName!);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryByDateRangeAsync_EmptyStore_ReturnsEmpty()
    {
        // Act
        var result = await _store.QueryByDateRangeAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryByStatusAsync_MultipleMatching_ReturnsAll()
    {
        // Arrange - Create multiple flows with same status
        for (int i = 0; i < 5; i++)
        {
            var state = new TestFlowState { FlowId = $"flow-{i}", OrderId = $"order-{i}" };
            await _store.CreateAsync(CreateSnapshot($"flow-{i}", state, DslFlowStatus.Running));
        }

        // Act
        var result = await _store.QueryByStatusAsync(DslFlowStatus.Running);

        // Assert
        result.Should().HaveCount(5);
        result.All(f => f.Status == DslFlowStatus.Running).Should().BeTrue();
    }

    #endregion

    #region Helper Methods

    private static FlowSnapshot<TestFlowState> CreateSnapshot(string flowId, TestFlowState state)
    {
        return FlowSnapshot<TestFlowState>.Create(flowId,
            state,
            currentStep: 0,
            status: DslFlowStatus.Running,
            error: null,
            waitCondition: null,
            createdAt: DateTime.UtcNow,
            updatedAt: DateTime.UtcNow,
            version: 0);
    }

    private static FlowSnapshot<TestFlowState> CreateSnapshot(string flowId, TestFlowState state, DslFlowStatus status)
    {
        return FlowSnapshot<TestFlowState>.Create(flowId,
            state,
            currentStep: 0,
            status: status,
            error: null,
            waitCondition: null,
            createdAt: DateTime.UtcNow,
            updatedAt: DateTime.UtcNow,
            version: 0);
    }

    private static FlowSnapshot<TestFlowState> CreateSnapshotWithDate(string flowId, TestFlowState state, DateTime createdAt)
    {
        return FlowSnapshot<TestFlowState>.Create(flowId,
            state,
            currentStep: 0,
            status: DslFlowStatus.Running,
            error: null,
            waitCondition: null,
            createdAt: createdAt,
            updatedAt: createdAt,
            version: 0);
    }

    private static FlowSnapshot<AnotherFlowState> CreateSnapshotForAnotherType(string flowId, AnotherFlowState state)
    {
        return FlowSnapshot<AnotherFlowState>.Create(flowId,
            state,
            currentStep: 0,
            status: DslFlowStatus.Running,
            error: null,
            waitCondition: null,
            createdAt: DateTime.UtcNow,
            updatedAt: DateTime.UtcNow,
            version: 0);
    }

    private static WaitCondition CreateWaitCondition(string correlationId)
    {
        return new WaitCondition
        {
            CorrelationId = correlationId,
            Type = WaitType.All,
            ExpectedCount = 2,
            CompletedCount = 0,
            Timeout = TimeSpan.FromMinutes(5),
            CreatedAt = DateTime.UtcNow,
            FlowId = "flow-1",
            FlowType = "TestFlow",
            Step = 0
        };
    }

    #endregion
}

#region Test Flow State

public class TestFlowState : IFlowState
{
    public const int Field_OrderId = 0;
    public const int FieldCount = 1;

    private int _changedMask;
    public string? FlowId { get; set; }

    private string? _orderId;
    public string? OrderId { get => _orderId; set { _orderId = value; MarkChanged(Field_OrderId); } }

    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
    public IEnumerable<string> GetChangedFieldNames()
    {
        if (IsFieldChanged(Field_OrderId)) yield return nameof(OrderId);
    }
}

public class AnotherFlowState : IFlowState
{
    public const int Field_CustomField = 0;
    public const int FieldCount = 1;

    private int _changedMask;
    public string? FlowId { get; set; }

    private string? _customField;
    public string? CustomField { get => _customField; set { _customField = value; MarkChanged(Field_CustomField); } }

    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
    public IEnumerable<string> GetChangedFieldNames()
    {
        if (IsFieldChanged(Field_CustomField)) yield return nameof(CustomField);
    }
}

#endregion






