using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Flow;

/// <summary>
/// Integration tests for ForEach functionality with different storage backends.
/// </summary>
public class ForEachIntegrationTests
{
    public class IntegrationState : IFlowState
    {
        public string? FlowId { get; set; }
        public List<string> Items { get; set; } = [];
        public Dictionary<string, string> Results { get; set; } = [];
        public int ProcessedCount { get; set; }

        private int _changedMask;
        public bool HasChanges => _changedMask != 0;
        public int GetChangedMask() => _changedMask;
        public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
        public void ClearChanges() => _changedMask = 0;
        public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
        public IEnumerable<string> GetChangedFieldNames() { yield break; }
    }

    public record ProcessItemCommand(string ItemId) : IRequest<string>
    {
        public long MessageId { get; init; } = DateTimeOffset.UtcNow.Ticks;
    }

    public class IntegrationFlow : FlowConfig<IntegrationState>
    {
        protected override void Configure(IFlowBuilder<IntegrationState> flow)
        {
            flow.Name("integration-foreach");

            flow.ForEach<string>(s => s.Items)
                .Configure((item, f) =>
                {
                    f.Send(s => new ProcessItemCommand(item))
                     .Into(s => s.Results[item]);
                })
                .WithBatchSize(5)
                .WithParallelism(1) // Sequential for predictable testing
                .OnItemSuccess((state, item, result) =>
                {
                    state.ProcessedCount++;
                })
            .EndForEach();
        }
    }

    [Fact]
    public async Task ForEach_Integration_ShouldWorkWithInMemoryStore()
    {
        // Arrange
        var mediator = CreateMockMediator();
        var store = new InMemoryDslFlowStore();
        var config = new IntegrationFlow();
        config.Build();

        var executor = new DslFlowExecutor<IntegrationState, IntegrationFlow>(mediator, store, config);

        var state = new IntegrationState
        {
            FlowId = "integration-001",
            Items = ["item1", "item2", "item3"]
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedCount.Should().Be(3);

        // Verify ForEach progress was cleared after completion
        var progress = await store.GetForEachProgressAsync("integration-001", 0);
        progress.Should().BeNull();
    }

    [Fact]
    public async Task ForEach_Integration_ShouldSupportRecovery()
    {
        // Arrange
        var store = new InMemoryDslFlowStore();

        // Simulate partial progress
        var progress = new ForEachProgress
        {
            CurrentIndex = 1,
            TotalCount = 3,
            CompletedIndices = [0],
            FailedIndices = []
        };

        await store.SaveForEachProgressAsync("recovery-001", 0, progress);

        var mediator = CreateMockMediator();
        var config = new IntegrationFlow();
        config.Build();

        var executor = new DslFlowExecutor<IntegrationState, IntegrationFlow>(mediator, store, config);

        var state = new IntegrationState
        {
            FlowId = "recovery-001",
            Items = ["item1", "item2", "item3"],
            ProcessedCount = 1 // Simulate already processed first item
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedCount.Should().Be(3); // Should process remaining items
    }

    [Fact]
    public async Task ForEach_Integration_ShouldHandleFailures()
    {
        // Arrange
        var mediator = CreateFailingMediator();
        var store = new InMemoryDslFlowStore();
        var config = new FailureHandlingFlow();
        config.Build();

        var executor = new DslFlowExecutor<IntegrationState, FailureHandlingFlow>(mediator, store, config);

        var state = new IntegrationState
        {
            FlowId = "failure-001",
            Items = ["item1", "FAIL", "item3"] // Second item will fail
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue(); // ContinueOnFailure
        result.State.ProcessedCount.Should().Be(2); // Only successful items
    }

    public class FailureHandlingFlow : FlowConfig<IntegrationState>
    {
        protected override void Configure(IFlowBuilder<IntegrationState> flow)
        {
            flow.Name("failure-handling-foreach");

            flow.ForEach<string>(s => s.Items)
                .Configure((item, f) =>
                {
                    f.Send(s => new ProcessItemCommand(item))
                     .Into(s => s.Results[item]);
                })
                .ContinueOnFailure() // Continue processing despite failures
                .OnItemSuccess((state, item, result) =>
                {
                    state.ProcessedCount++;
                })
            .EndForEach();
        }
    }

    [Fact]
    public async Task ForEach_Integration_ShouldWorkWithDifferentBatchSizes()
    {
        // Test different batch sizes to ensure batching logic works
        var testCases = new[] { 1, 2, 5, 10 };

        foreach (var batchSize in testCases)
        {
            // Arrange
            var mediator = CreateMockMediator();
            var store = new InMemoryDslFlowStore();
            var config = new BatchTestFlow(batchSize);
            config.Build();

            var executor = new DslFlowExecutor<IntegrationState, BatchTestFlow>(mediator, store, config);

            var items = Enumerable.Range(1, 7).Select(i => $"item{i}").ToList();
            var state = new IntegrationState
            {
                FlowId = $"batch-{batchSize}",
                Items = items
            };

            // Act
            var result = await executor.RunAsync(state);

            // Assert
            result.IsSuccess.Should().BeTrue($"Batch size {batchSize} should work");
            result.State.ProcessedCount.Should().Be(7, $"All items should be processed with batch size {batchSize}");
        }
    }

    public class BatchTestFlow : FlowConfig<IntegrationState>
    {
        private readonly int _batchSize;

        public BatchTestFlow(int batchSize)
        {
            _batchSize = batchSize;
        }

        protected override void Configure(IFlowBuilder<IntegrationState> flow)
        {
            flow.Name("batch-test-foreach");

            flow.ForEach<string>(s => s.Items)
                .Configure((item, f) =>
                {
                    f.Send(s => new ProcessItemCommand(item))
                     .Into(s => s.Results[item]);
                })
                .WithBatchSize(_batchSize)
                .OnItemSuccess((state, item, result) =>
                {
                    state.ProcessedCount++;
                })
            .EndForEach();
        }
    }

    private static ICatgaMediator CreateMockMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<ProcessItemCommand, string>(
            Arg.Any<ProcessItemCommand>(),
            Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessItemCommand>();
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success($"processed-{request.ItemId}"));
            });

        return mediator;
    }

    private static ICatgaMediator CreateFailingMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<ProcessItemCommand, string>(
            Arg.Any<ProcessItemCommand>(),
            Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessItemCommand>();

                if (request.ItemId == "FAIL")
                {
                    return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Failure("Simulated failure"));
                }

                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success($"processed-{request.ItemId}"));
            });

        return mediator;
    }
}
