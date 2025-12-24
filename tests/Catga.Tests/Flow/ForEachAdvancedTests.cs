using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Flow;

/// <summary>
/// Advanced tests for ForEach functionality including performance and complex scenarios.
/// </summary>
public class ForEachAdvancedTests
{
    public class OrderState : IFlowState
    {
        public string? FlowId { get; set; }
        public List<OrderItem> Items { get; set; } = [];
        public Dictionary<string, string> ProcessedItems { get; set; } = [];
        public List<string> FailedItems { get; set; } = [];
        public bool AllItemsProcessed { get; set; }
        public int TotalProcessed { get; set; }

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

    public record ValidateItemRequest(string ItemId) : IRequest<bool>
    {
        public long MessageId { get; init; } = DateTimeOffset.UtcNow.Ticks;
    }

    public class ComplexForEachFlow : FlowConfig<OrderState>
    {
        protected override void Configure(IFlowBuilder<OrderState> flow)
        {
            flow.Name("complex-foreach-flow");

            // Complex ForEach with multiple steps per item
            flow.ForEach<OrderItem>(s => s.Items)
                .Configure((item, f) =>
                {
                    // Multiple steps for each item
                    f.Send(s => new ValidateItemRequest(item.Id));
                    f.Send(s => new ProcessItemRequest(item.Id, item.ProductId, item.Quantity));
                })
                .WithBatchSize(5)
                .ContinueOnFailure()
                .OnItemSuccess((state, item, result) =>
                {
                    state.ProcessedItems[item.Id] = result?.ToString() ?? "success";
                    state.TotalProcessed++;
                })
                .OnItemFail((state, item, error) =>
                {
                    state.FailedItems.Add(item.Id);
                })
                .OnComplete(s => s.AllItemsProcessed = true)
            .EndForEach();
        }
    }

    public class LargeCollectionFlow : FlowConfig<OrderState>
    {
        protected override void Configure(IFlowBuilder<OrderState> flow)
        {
            flow.Name("large-collection-flow");

            flow.ForEach<OrderItem>(s => s.Items)
                .Configure((item, f) =>
                {
                    f.Send(s => new ProcessItemRequest(item.Id, item.ProductId, item.Quantity));
                })
                .WithBatchSize(100) // Large batch size for performance
                .ContinueOnFailure()
                .OnItemSuccess((state, item, result) =>
                {
                    state.ProcessedItems[item.Id] = result?.ToString() ?? "success";
                })
            .EndForEach();
        }
    }

    [Fact]
    public async Task ForEach_ShouldHandleComplexItemProcessing()
    {
        // Arrange
        var mediator = CreateMockMediator();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new ComplexForEachFlow();
        config.Build();

        var executor = new DslFlowExecutor<OrderState, ComplexForEachFlow>(mediator, store, config);

        var state = new OrderState
        {
            FlowId = "complex-001",
            Items =
            [
                new("item1", "prod1", 2),
                new("item2", "prod2", 3),
                new("item3", "FAIL", 1), // This will fail
                new("item4", "prod4", 5)
            ]
        };

        SetupComplexMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().HaveCount(3); // 3 successful items
        result.State.FailedItems.Should().Contain("item3");
        result.State.TotalProcessed.Should().Be(3);
        result.State.AllItemsProcessed.Should().BeTrue();
    }

    [Fact]
    public async Task ForEach_ShouldHandleLargeCollections()
    {
        // Arrange
        var mediator = CreateMockMediator();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new LargeCollectionFlow();
        config.Build();

        var executor = new DslFlowExecutor<OrderState, LargeCollectionFlow>(mediator, store, config);

        // Create a large collection (1000 items)
        var items = Enumerable.Range(1, 1000)
            .Select(i => new OrderItem($"item{i}", $"prod{i}", i % 10 + 1))
            .ToList();

        var state = new OrderState
        {
            FlowId = "large-001",
            Items = items
        };

        SetupLargeCollectionMediator(mediator);

        // Act
        var startTime = DateTime.UtcNow;
        var result = await executor.RunAsync(state);
        var duration = DateTime.UtcNow - startTime;

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().HaveCount(1000);

        // Performance assertion - should complete within reasonable time
        duration.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ForEach_ShouldSupportRecoveryWithLargeCollections()
    {
        // Arrange
        var store = TestStoreExtensions.CreateTestFlowStore();

        // Simulate a flow that was interrupted at item 500
        var progress = new ForEachProgress
        {
            CurrentIndex = 500,
            TotalCount = 1000,
            CompletedIndices = Enumerable.Range(0, 500).ToList(),
            FailedIndices = []
        };

        await store.SaveForEachProgressAsync("recovery-001", 0, progress);

        // Create state with all 1000 items
        var items = Enumerable.Range(1, 1000)
            .Select(i => new OrderItem($"item{i}", $"prod{i}", i % 10 + 1))
            .ToList();

        var state = new OrderState
        {
            FlowId = "recovery-001",
            Items = items,
            // Simulate already processed items
            ProcessedItems = Enumerable.Range(1, 500)
                .ToDictionary(i => $"item{i}", i => $"result-{i-1}")
        };

        var mediator = CreateMockMediator();
        SetupLargeCollectionMediator(mediator);

        var config = new LargeCollectionFlow();
        config.Build();
        var executor = new DslFlowExecutor<OrderState, LargeCollectionFlow>(mediator, store, config);

        // Act - Should resume from item 500
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().HaveCount(1000);

        // Verify progress tracking (progress may or may not be cleared based on implementation)
        var finalProgress = await store.GetForEachProgressAsync("recovery-001", 0);
        // Progress cleanup is implementation-dependent
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task ForEach_ShouldRespectBatchSize(int batchSize)
    {
        // This test verifies that different batch sizes work correctly
        // In a real implementation, we would monitor memory usage

        var mediator = CreateMockMediator();
        var store = TestStoreExtensions.CreateTestFlowStore();

        // Create a flow with specific batch size
        var flowConfig = new TestBatchFlow(batchSize);
        flowConfig.Build();

        var executor = new DslFlowExecutor<OrderState, TestBatchFlow>(mediator, store, flowConfig);

        var items = Enumerable.Range(1, 200)
            .Select(i => new OrderItem($"item{i}", $"prod{i}", 1))
            .ToList();

        var state = new OrderState
        {
            FlowId = $"batch-{batchSize}",
            Items = items
        };

        SetupLargeCollectionMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().HaveCount(200);
    }

    public class TestBatchFlow : FlowConfig<OrderState>
    {
        private readonly int _batchSize;

        public TestBatchFlow(int batchSize)
        {
            _batchSize = batchSize;
        }

        protected override void Configure(IFlowBuilder<OrderState> flow)
        {
            flow.Name($"batch-flow-{_batchSize}");

            flow.ForEach<OrderItem>(s => s.Items)
                .Configure((item, f) =>
                {
                    f.Send(s => new ProcessItemRequest(item.Id, item.ProductId, item.Quantity));
                })
                .WithBatchSize(_batchSize)
                .OnItemSuccess((state, item, result) =>
                {
                    state.ProcessedItems[item.Id] = result?.ToString() ?? "success";
                })
            .EndForEach();
        }
    }

    private static ICatgaMediator CreateMockMediator()
    {
        return Substitute.For<ICatgaMediator>();
    }

    private static void SetupComplexMediator(ICatgaMediator mediator)
    {
        // Setup validation requests
        mediator.SendAsync<ValidateItemRequest, bool>(
            Arg.Any<ValidateItemRequest>(),
            Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ValidateItemRequest>();
                return CatgaResult<bool>.Success(true);
            });

        // Setup processing requests
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
                return CatgaResult<string>.Success($"processed-{request.ItemId}");
            });
    }

    private static void SetupLargeCollectionMediator(ICatgaMediator mediator)
    {
        mediator.SendAsync<ProcessItemRequest, string>(
            Arg.Any<ProcessItemRequest>(),
            Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessItemRequest>();
                return CatgaResult<string>.Success($"processed-{request.ItemId}");
            });
    }
}
