using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Flow;

/// <summary>
/// TDD tests for ForEach Flow DSL functionality.
/// </summary>
public class ForEachFlowDslTests
{
    #region Test State and Messages

    public class OrderFlowState : IFlowState
    {
        public string? FlowId { get; set; }
        public string? OrderId { get; set; }
        public List<OrderItem> Items { get; set; } = [];
        public Dictionary<string, string> ProcessedItems { get; set; } = [];
        public List<string> FailedItems { get; set; } = [];
        public bool AllItemsProcessed { get; set; }

        private int _changedMask;
        public bool HasChanges => _changedMask != 0;
        public int GetChangedMask() => _changedMask;
        public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
        public void ClearChanges() => _changedMask = 0;
        public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
        public IEnumerable<string> GetChangedFieldNames() { yield break; }
    }

    public record OrderItem(string Id, string ProductId, int Quantity);

    public record ProcessItemRequest(string ItemId, string ProductId, int Quantity) : IRequest<string>
    {
        public long MessageId { get; init; } = DateTimeOffset.UtcNow.Ticks;
    }
    public record ProcessItemResponse(string ItemId, string Result);

    #endregion

    #region API Tests

    [Fact]
    public void ForEach_API_ShouldBeAvailableOnFlowBuilder()
    {
        // Arrange
        var config = new TestForEachFlow();

        // Act & Assert - Should compile without errors
        Assert.NotNull(config);
    }

    [Fact]
    public void ForEach_API_ShouldSupportGenericItemType()
    {
        // Arrange
        var config = new TestForEachFlow();
        config.Build(); // Build the configuration
        var steps = config.Steps;

        // Act & Assert
        steps.Should().HaveCount(1);
        steps[0].Type.Should().Be(StepType.ForEach);
    }

    [Fact]
    public void ForEach_API_ShouldSupportBatchSize()
    {
        // Arrange
        var config = new BatchForEachFlow();
        config.Build(); // Build the configuration
        var forEachStep = config.Steps[0];

        // Act & Assert
        forEachStep.BatchSize.Should().Be(5);
    }

    [Fact]
    public void ForEach_API_ShouldSupportFailureHandling()
    {
        // Arrange
        var config = new FailureHandlingForEachFlow();
        config.Build(); // Build the configuration
        var forEachStep = config.Steps[0];

        // Act & Assert
        forEachStep.FailureHandling.Should().Be(ForEachFailureHandling.ContinueOnFailure);
    }

    #endregion

    #region Execution Tests

    [Fact]
    public async Task ForEach_Execution_ShouldProcessAllItems()
    {
        // Arrange
        var mediator = CreateMockMediator();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new TestForEachFlow();
        var executor = new DslFlowExecutor<OrderFlowState, TestForEachFlow>(mediator, store, config);

        var state = new OrderFlowState
        {
            OrderId = "ORD-001",
            Items =
            [
                new("item1", "prod1", 2),
                new("item2", "prod2", 3),
                new("item3", "prod3", 1)
            ]
        };

        SetupMediatorForItems(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().HaveCount(3);
        result.State.ProcessedItems.Should().ContainKeys("item1", "item2", "item3");
    }

    [Fact]
    public async Task ForEach_Execution_ShouldHandleEmptyCollection()
    {
        // Arrange
        var mediator = CreateMockMediator();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new TestForEachFlow();
        var executor = new DslFlowExecutor<OrderFlowState, TestForEachFlow>(mediator, store, config);

        var state = new OrderFlowState
        {
            OrderId = "ORD-002",
            Items = [] // Empty collection
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().BeEmpty();
        result.State.AllItemsProcessed.Should().BeTrue();
    }

    [Fact]
    public async Task ForEach_Execution_ShouldContinueOnFailure_WhenConfigured()
    {
        // Arrange
        var mediator = CreateMockMediator();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new FailureHandlingForEachFlow();
        var executor = new DslFlowExecutor<OrderFlowState, FailureHandlingForEachFlow>(mediator, store, config);

        var state = new OrderFlowState
        {
            OrderId = "ORD-003",
            Items =
            [
                new("item1", "prod1", 2),
                new("item2", "FAIL", 3), // This will fail
                new("item3", "prod3", 1)
            ]
        };

        SetupMediatorForItemsWithFailure(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().HaveCount(2); // Only successful items
        result.State.FailedItems.Should().Contain("item2");
    }

    #endregion

    #region Recovery Tests

    [Fact]
    public async Task ForEach_Recovery_ShouldResumeFromCorrectPosition()
    {
        // Arrange
        var store = TestStoreExtensions.CreateTestFlowStore();
        var state = new OrderFlowState
        {
            FlowId = "flow-foreach-001",
            OrderId = "ORD-REC-001",
            Items =
            [
                new("item1", "prod1", 2),
                new("item2", "prod2", 3),
                new("item3", "prod3", 1)
            ],
            ProcessedItems = { ["item1"] = "result1" } // Already processed first item
        };

        // Create snapshot at ForEach position [0, 1] (step 0, processing item 1)
        var forEachPosition = new FlowPosition([0, 1]);
        var snapshot = new FlowSnapshot<OrderFlowState>
        {
            FlowId = "flow-foreach-001",
            State = state,
            Position = forEachPosition,
            Status = DslFlowStatus.Running,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        // Act
        await store.CreateAsync(snapshot);
        var retrieved = await store.GetAsync<OrderFlowState>("flow-foreach-001");

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Position.Path.Should().BeEquivalentTo(new[] { 0, 1 });
        retrieved.State.ProcessedItems.Should().ContainKey("item1");
    }

    [Fact]
    public void ForEachProgress_ShouldTrackProcessingState()
    {
        // Arrange & Act
        var progress = new ForEachProgress
        {
            CurrentIndex = 2,
            TotalCount = 5,
            CompletedIndices = [0, 1],
            FailedIndices = [3]
        };

        // Assert
        progress.CurrentIndex.Should().Be(2);
        progress.TotalCount.Should().Be(5);
        progress.CompletedIndices.Should().BeEquivalentTo([0, 1]);
        progress.FailedIndices.Should().Contain(3);
    }

    #endregion

    #region Test Flow Configurations

    public class TestForEachFlow : FlowConfig<OrderFlowState>
    {
        protected override void Configure(IFlowBuilder<OrderFlowState> flow)
        {
            flow.Name("test-foreach-flow");

            flow.ForEach<OrderItem>(s => s.Items)
                .Configure((item, f) =>
                {
                    f.Send(s => new ProcessItemRequest(item.Id, item.ProductId, item.Quantity));
                })
                .OnItemSuccess((state, item, result) =>
                {
                    state.ProcessedItems[item.Id] = result?.ToString() ?? "success";
                })
                .OnComplete(s => s.AllItemsProcessed = true)
                .EndForEach();
        }
    }

    public class BatchForEachFlow : FlowConfig<OrderFlowState>
    {
        protected override void Configure(IFlowBuilder<OrderFlowState> flow)
        {
            flow.Name("batch-foreach-flow");

            flow.ForEach<OrderItem>(s => s.Items)
                .Configure((item, f) =>
                {
                    f.Send(s => new ProcessItemRequest(item.Id, item.ProductId, item.Quantity));
                })
                .WithBatchSize(5)
                .EndForEach();
        }
    }

    public class FailureHandlingForEachFlow : FlowConfig<OrderFlowState>
    {
        protected override void Configure(IFlowBuilder<OrderFlowState> flow)
        {
            flow.Name("failure-handling-foreach-flow");

            flow.ForEach<OrderItem>(s => s.Items)
                .Configure((item, f) =>
                {
                    f.Send(s => new ProcessItemRequest(item.Id, item.ProductId, item.Quantity));
                })
                .ContinueOnFailure()
                .OnItemSuccess((state, item, result) =>
                {
                    state.ProcessedItems[item.Id] = result?.ToString() ?? "success";
                })
                .OnItemFail((state, item, error) => state.FailedItems.Add(item.Id))
                .EndForEach();
        }
    }

    #endregion

    #region Mock Helpers

    private static ICatgaMediator CreateMockMediator()
    {
        return Substitute.For<ICatgaMediator>();
    }

    private static void SetupMediatorForItems(ICatgaMediator mediator)
    {
        mediator.SendAsync<ProcessItemRequest, string>(
            Arg.Any<ProcessItemRequest>(),
            Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessItemRequest>();
                return CatgaResult<string>.Success($"result-{request.ItemId}");
            });
    }

    private static void SetupMediatorForItemsWithFailure(ICatgaMediator mediator)
    {
        mediator.SendAsync<ProcessItemRequest, string>(
            Arg.Any<ProcessItemRequest>(),
            Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessItemRequest>();
                if (request.ProductId == "FAIL")
                {
                    return CatgaResult<string>.Failure("Simulated failure");
                }
                return CatgaResult<string>.Success($"result-{request.ItemId}");
            });
    }

    #endregion
}
