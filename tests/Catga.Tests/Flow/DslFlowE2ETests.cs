using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow;

/// <summary>
/// End-to-end tests for DSL Flow persistence functionality.
/// </summary>
public class DslFlowE2ETests
{
    #region Flow Persistence E2E Tests

    [Fact]
    public async Task E2E_FlowPersistence_SavesAndRestoresState()
    {
        // Arrange
        var store = new InMemoryDslFlowStore();

        var state = new TestFlowState { OrderId = "ORD-005", Amount = 100 };
        var snapshot = new FlowSnapshot<TestFlowState>(
            FlowId: "flow-001",
            State: state,
            CurrentStep: 2,
            Status: DslFlowStatus.Running,
            Error: null,
            WaitCondition: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            Version: 1
        );

        // Act
        var created = await store.CreateAsync(snapshot);
        var retrieved = await store.GetAsync<TestFlowState>("flow-001");

        // Assert
        created.Should().BeTrue();
        retrieved.Should().NotBeNull();
        retrieved!.State.OrderId.Should().Be("ORD-005");
        retrieved.CurrentStep.Should().Be(2);
    }

    [Fact]
    public async Task E2E_FlowPersistence_UpdatesState()
    {
        // Arrange
        var store = new InMemoryDslFlowStore();

        var state = new TestFlowState { OrderId = "ORD-006", Amount = 100 };
        var snapshot = new FlowSnapshot<TestFlowState>(
            FlowId: "flow-002",
            State: state,
            CurrentStep: 0,
            Status: DslFlowStatus.Running,
            Error: null,
            WaitCondition: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            Version: 1
        );

        await store.CreateAsync(snapshot);

        // Act - Update state
        state.Amount = 200;
        state.MarkChanged(0);
        var updatedSnapshot = snapshot with { CurrentStep = 3, Status = DslFlowStatus.Completed, UpdatedAt = DateTime.UtcNow };

        var updated = await store.UpdateAsync(updatedSnapshot);
        var retrieved = await store.GetAsync<TestFlowState>("flow-002");

        // Assert
        updated.Should().BeTrue();
        retrieved!.CurrentStep.Should().Be(3);
        retrieved.Status.Should().Be(DslFlowStatus.Completed);
    }

    [Fact]
    public async Task E2E_FlowPersistence_DeletesFlow()
    {
        // Arrange
        var store = new InMemoryDslFlowStore();

        var state = new TestFlowState { OrderId = "ORD-007" };
        var snapshot = new FlowSnapshot<TestFlowState>(
            FlowId: "flow-003",
            State: state,
            CurrentStep: 0,
            Status: DslFlowStatus.Running,
            Error: null,
            WaitCondition: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            Version: 1
        );

        await store.CreateAsync(snapshot);

        // Act
        var deleted = await store.DeleteAsync("flow-003");
        var retrieved = await store.GetAsync<TestFlowState>("flow-003");

        // Assert
        deleted.Should().BeTrue();
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task E2E_FlowPersistence_WaitCondition_SavesAndRestores()
    {
        // Arrange
        var store = new InMemoryDslFlowStore();
        var correlationId = "corr-001";
        var waitCondition = new WaitCondition
        {
            CorrelationId = correlationId,
            Type = WaitType.All,
            ExpectedCount = 3,
            CompletedCount = 0,
            Timeout = TimeSpan.FromMinutes(5),
            CreatedAt = DateTime.UtcNow,
            FlowId = "flow-wait-001",
            FlowType = "TestFlow",
            Step = 0
        };

        // Act
        await store.SetWaitConditionAsync(correlationId, waitCondition);
        var retrieved = await store.GetWaitConditionAsync(correlationId);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Type.Should().Be(WaitType.All);
        retrieved.ExpectedCount.Should().Be(3);
    }

    [Fact]
    public async Task E2E_FlowPersistence_WaitCondition_Updates()
    {
        // Arrange
        var store = new InMemoryDslFlowStore();
        var correlationId = "corr-002";
        var waitCondition = new WaitCondition
        {
            CorrelationId = correlationId,
            Type = WaitType.Any,
            ExpectedCount = 2,
            CompletedCount = 0,
            Timeout = TimeSpan.FromMinutes(5),
            CreatedAt = DateTime.UtcNow,
            FlowId = "flow-wait-002",
            FlowType = "TestFlow",
            Step = 1
        };

        await store.SetWaitConditionAsync(correlationId, waitCondition);

        // Act - Update
        waitCondition.CompletedCount = 1;
        waitCondition.Results.Add(new FlowCompletedEventData
        {
            FlowId = "child-1",
            Success = true
        });
        await store.UpdateWaitConditionAsync(correlationId, waitCondition);

        var retrieved = await store.GetWaitConditionAsync(correlationId);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.CompletedCount.Should().Be(1);
        retrieved.Results.Should().HaveCount(1);
    }

    [Fact]
    public async Task E2E_FlowPersistence_MultipleFlows_IndependentState()
    {
        // Arrange
        var store = new InMemoryDslFlowStore();

        var state1 = new TestFlowState { OrderId = "ORD-A", Amount = 100 };
        var state2 = new TestFlowState { OrderId = "ORD-B", Amount = 200 };

        var snapshot1 = new FlowSnapshot<TestFlowState>(
            FlowId: "flow-a",
            State: state1,
            CurrentStep: 1,
            Status: DslFlowStatus.Running,
            Error: null,
            WaitCondition: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            Version: 1
        );

        var snapshot2 = new FlowSnapshot<TestFlowState>(
            FlowId: "flow-b",
            State: state2,
            CurrentStep: 2,
            Status: DslFlowStatus.Completed,
            Error: null,
            WaitCondition: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            Version: 1
        );

        // Act
        await store.CreateAsync(snapshot1);
        await store.CreateAsync(snapshot2);

        var retrieved1 = await store.GetAsync<TestFlowState>("flow-a");
        var retrieved2 = await store.GetAsync<TestFlowState>("flow-b");

        // Assert
        retrieved1!.State.OrderId.Should().Be("ORD-A");
        retrieved1.CurrentStep.Should().Be(1);
        retrieved1.Status.Should().Be(DslFlowStatus.Running);

        retrieved2!.State.OrderId.Should().Be("ORD-B");
        retrieved2.CurrentStep.Should().Be(2);
        retrieved2.Status.Should().Be(DslFlowStatus.Completed);
    }

    [Fact]
    public async Task E2E_FlowPersistence_ConcurrentUpdates_LastWriteWins()
    {
        // Arrange
        var store = new InMemoryDslFlowStore();

        var state = new TestFlowState { OrderId = "ORD-CONCURRENT" };
        var snapshot = new FlowSnapshot<TestFlowState>(
            FlowId: "flow-concurrent",
            State: state,
            CurrentStep: 0,
            Status: DslFlowStatus.Running,
            Error: null,
            WaitCondition: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            Version: 1
        );

        await store.CreateAsync(snapshot);

        // Act - Simulate concurrent updates
        var tasks = Enumerable.Range(1, 5).Select(async i =>
        {
            var updated = snapshot with { CurrentStep = i, UpdatedAt = DateTime.UtcNow };
            await store.UpdateAsync(updated);
        });

        await Task.WhenAll(tasks);

        var retrieved = await store.GetAsync<TestFlowState>("flow-concurrent");

        // Assert - One of the updates should have succeeded
        retrieved.Should().NotBeNull();
        retrieved!.CurrentStep.Should().BeInRange(1, 5);
    }

    #endregion

    #region Test State

    public class TestFlowState : IFlowState
    {
        public string? FlowId { get; set; }
        public string? OrderId { get; set; }
        public decimal Amount { get; set; }

        private int _changedMask;
        public bool HasChanges => _changedMask != 0;
        public int GetChangedMask() => _changedMask;
        public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
        public void ClearChanges() => _changedMask = 0;
        public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
        public IEnumerable<string> GetChangedFieldNames() { yield break; }
    }

    #endregion
}
