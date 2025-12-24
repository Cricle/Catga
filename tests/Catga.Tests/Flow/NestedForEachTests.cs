using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Flow;

/// <summary>
/// Tests for nested ForEach functionality and complex hierarchical processing.
/// </summary>
public class NestedForEachTests
{
    // Domain Models
    public record Order(string Id, List<OrderLine> Lines);
    public record OrderLine(string Id, string ProductId, List<OrderItem> Items);
    public record OrderItem(string Id, string Sku, int Quantity);

    // Commands
    public record ProcessOrderRequest(string OrderId) : IRequest<string>
    {
        public long MessageId { get; init; } = DateTimeOffset.UtcNow.Ticks;
    }

    public record ProcessLineRequest(string LineId, string ProductId) : IRequest<string>
    {
        public long MessageId { get; init; } = DateTimeOffset.UtcNow.Ticks;
    }

    public record ProcessItemRequest(string ItemId, string Sku, int Quantity) : IRequest<string>
    {
        public long MessageId { get; init; } = DateTimeOffset.UtcNow.Ticks;
    }

    // State
    public class NestedProcessingState : IFlowState
    {
        public string? FlowId { get; set; }
        public List<Order> Orders { get; set; } = [];
        public Dictionary<string, string> OrderResults { get; set; } = [];
        public Dictionary<string, string> LineResults { get; set; } = [];
        public Dictionary<string, string> ItemResults { get; set; } = [];
        public int TotalProcessed { get; set; }
        public List<string> ProcessingLog { get; set; } = [];

        private int _changedMask;
        public bool HasChanges => _changedMask != 0;
        public int GetChangedMask() => _changedMask;
        public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
        public void ClearChanges() => _changedMask = 0;
        public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
        public IEnumerable<string> GetChangedFieldNames() { yield break; }
    }

    /// <summary>
    /// Simple nested ForEach: Orders -> Lines -> Items
    /// </summary>
    public class SimpleNestedForEachFlow : FlowConfig<NestedProcessingState>
    {
        protected override void Configure(IFlowBuilder<NestedProcessingState> flow)
        {
            flow.Name("simple-nested-foreach");

            // Level 1: Process Orders
            flow.ForEach<Order>(s => s.Orders)
                .Configure((order, f) =>
                {
                    f.Send(s => new ProcessOrderRequest(order.Id))
                     .Into(s => s.OrderResults[order.Id]);

                    // Process all items from all lines in this order
                    var allItems = order.Lines.SelectMany(line => line.Items).ToList();
                    f.ForEach<OrderItem>(s => allItems)
                        .Configure((item, f2) =>
                        {
                            f2.Send(s => new ProcessItemRequest(item.Id, item.Sku, item.Quantity))
                              .Into(s => s.ItemResults[item.Id]);
                        })
                        .WithBatchSize(5)
                        .OnItemSuccess((state, item, result) =>
                        {
                            state.TotalProcessed++;
                            state.ProcessingLog.Add($"Item {item.Id} processed");
                        })
                    .EndForEach();
                })
                .WithBatchSize(2)
                .OnItemSuccess((state, order, result) =>
                {
                    state.ProcessingLog.Add($"Order {order.Id} processed");
                })
            .EndForEach();
        }
    }

    /// <summary>
    /// Conditional nested ForEach with error handling
    /// </summary>
    public class ConditionalNestedForEachFlow : FlowConfig<NestedProcessingState>
    {
        protected override void Configure(IFlowBuilder<NestedProcessingState> flow)
        {
            flow.Name("conditional-nested-foreach");

            flow.ForEach<Order>(s => s.Orders)
                .Configure((order, f) =>
                {
                    f.Send(s => new ProcessOrderRequest(order.Id))
                     .Into(s => s.OrderResults[order.Id]);

                    // Process items from all lines (filtered by quantity > 0)
                    var validItems = order.Lines.SelectMany(line => line.Items.Where(i => i.Quantity > 0)).ToList();
                    f.ForEach<OrderItem>(s => validItems)
                        .Configure((item, f2) =>
                        {
                            f2.Send(s => new ProcessItemRequest(item.Id, item.Sku, item.Quantity))
                              .Into(s => s.ItemResults[item.Id]);
                        })
                        .ContinueOnFailure()
                        .OnItemSuccess((state, item, result) =>
                        {
                            state.TotalProcessed++;
                        })
                    .EndForEach();
                })
                .ContinueOnFailure()
            .EndForEach();
        }
    }

    [Fact]
    public async Task NestedForEach_ShouldProcessHierarchicalData()
    {
        // Arrange
        var mediator = CreateMockMediator();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new SimpleNestedForEachFlow();
        config.Build();

        var executor = new DslFlowExecutor<NestedProcessingState, SimpleNestedForEachFlow>(mediator, store, config);

        var state = new NestedProcessingState
        {
            FlowId = "nested-001",
            Orders =
            [
                new Order("order1",
                [
                    new OrderLine("line1", "prod1",
                    [
                        new OrderItem("item1", "sku1", 2),
                        new OrderItem("item2", "sku2", 3)
                    ]),
                    new OrderLine("line2", "prod2",
                    [
                        new OrderItem("item3", "sku3", 1)
                    ])
                ]),
                new Order("order2",
                [
                    new OrderLine("line3", "prod3",
                    [
                        new OrderItem("item4", "sku4", 5),
                        new OrderItem("item5", "sku5", 2)
                    ])
                ])
            ]
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify all levels were processed
        result.State.OrderResults.Should().HaveCount(2);
        result.State.ItemResults.Should().HaveCount(5);
        result.State.TotalProcessed.Should().Be(5);

        // Verify processing order in logs
        result.State.ProcessingLog.Should().Contain("Order order1 processed");
        result.State.ProcessingLog.Should().Contain("Order order2 processed");
        result.State.ProcessingLog.Should().Contain("Item item1 processed");
        result.State.ProcessingLog.Should().Contain("Item item2 processed");
        result.State.ProcessingLog.Should().Contain("Item item3 processed");
        result.State.ProcessingLog.Should().Contain("Item item4 processed");
        result.State.ProcessingLog.Should().Contain("Item item5 processed");
    }

    [Fact]
    public async Task ConditionalNestedForEach_ShouldHandleComplexLogic()
    {
        // Arrange
        var mediator = CreateMockMediator();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new ConditionalNestedForEachFlow();
        config.Build();

        var executor = new DslFlowExecutor<NestedProcessingState, ConditionalNestedForEachFlow>(mediator, store, config);

        var state = new NestedProcessingState
        {
            FlowId = "conditional-001",
            Orders =
            [
                new Order("order1",
                [
                    new OrderLine("line1", "prod1",
                    [
                        new OrderItem("item1", "sku1", 2),
                        new OrderItem("item2", "sku2", 0), // Zero quantity - should be filtered
                        new OrderItem("item3", "sku3", 3)
                    ])
                ])
            ]
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.OrderResults.Should().HaveCount(1);
        result.State.ItemResults.Should().HaveCount(2); // Only items with quantity > 0
        result.State.TotalProcessed.Should().Be(2);
    }

    [Fact]
    public async Task NestedForEach_ShouldSupportRecovery()
    {
        // Test that nested ForEach supports recovery at different levels
        var store = TestStoreExtensions.CreateTestFlowStore();

        // Simulate interruption during nested processing
        // Outer ForEach progress: completed order1, working on order2
        var outerProgress = new ForEachProgress
        {
            CurrentIndex = 1,
            TotalCount = 2,
            CompletedIndices = [0],
            FailedIndices = []
        };

        await store.SaveForEachProgressAsync("recovery-nested", 0, outerProgress);

        var mediator = CreateMockMediator();
        var config = new SimpleNestedForEachFlow();
        config.Build();

        var executor = new DslFlowExecutor<NestedProcessingState, SimpleNestedForEachFlow>(mediator, store, config);

        var state = new NestedProcessingState
        {
            FlowId = "recovery-nested",
            Orders =
            [
                new Order("order1",
                [
                    new OrderLine("line1", "prod1",
                    [
                        new OrderItem("item1", "sku1", 1)
                    ])
                ]),
                new Order("order2",
                [
                    new OrderLine("line2", "prod2",
                    [
                        new OrderItem("item2", "sku2", 1)
                    ])
                ])
            ],
            // Simulate already processed first order
            OrderResults = new Dictionary<string, string> { ["order1"] = "processed" },
            LineResults = new Dictionary<string, string> { ["line1"] = "processed" },
            ItemResults = new Dictionary<string, string> { ["item1"] = "processed" },
            TotalProcessed = 1
        };

        // Act - Should resume from order2
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.OrderResults.Should().HaveCount(2);
        result.State.TotalProcessed.Should().Be(2);
    }

    private static ICatgaMediator CreateMockMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<ProcessOrderRequest, string>(
            Arg.Any<ProcessOrderRequest>(),
            Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessOrderRequest>();
                return CatgaResult<string>.Success($"order-{request.OrderId}-processed");
            });

        mediator.SendAsync<ProcessLineRequest, string>(
            Arg.Any<ProcessLineRequest>(),
            Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessLineRequest>();
                return CatgaResult<string>.Success($"line-{request.LineId}-processed");
            });

        mediator.SendAsync<ProcessItemRequest, string>(
            Arg.Any<ProcessItemRequest>(),
            Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessItemRequest>();
                return CatgaResult<string>.Success($"item-{request.ItemId}-processed");
            });

        return mediator;
    }
}
