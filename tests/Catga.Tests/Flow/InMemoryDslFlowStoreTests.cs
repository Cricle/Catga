using Catga.Flow.Dsl;
using FluentAssertions;

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
        var updated = snapshot with { State = newState, Status = DslFlowStatus.Completed };
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

        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            var newState = new TestFlowState { FlowId = "flow-1", OrderId = $"order-{i}" };
            var updated = snapshot with { State = newState, Version = i };
            return await _store.UpdateAsync(updated);
        }).ToArray();

        var results = await Task.WhenAll(tasks);

        results.All(r => r).Should().BeTrue();
    }

    #endregion

    #region Helper Methods

    private static FlowSnapshot<TestFlowState> CreateSnapshot(string flowId, TestFlowState state)
    {
        return new FlowSnapshot<TestFlowState>(
            flowId,
            state,
            CurrentStep: 0,
            Status: DslFlowStatus.Running,
            Error: null,
            WaitCondition: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            Version: 0);
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

#endregion
