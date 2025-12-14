using Catga.Abstractions;
using Catga.Flow;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Flow.Scheduling.RealWorldScenarios;

/// <summary>
/// E2E tests for concurrent and parallel workflow scenarios with scheduling.
/// </summary>
public class ConcurrentWorkflowE2ETests
{
    #region Test Infrastructure

    public class BatchProcessState : IFlowState
    {
        public string? FlowId { get; set; }
        public string BatchId { get; set; } = $"BATCH-{Guid.NewGuid():N}"[..12];
        public List<string> Items { get; set; } = [];
        public int ProcessedCount { get; set; }
        public int FailedCount { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
    }

    public class SagaState : IFlowState
    {
        public string? FlowId { get; set; }
        public string TransactionId { get; set; } = Guid.NewGuid().ToString("N");
        public decimal Amount { get; set; }
        public bool Step1Completed { get; set; }
        public bool Step2Completed { get; set; }
        public bool Step3Completed { get; set; }
        public bool NeedsCompensation { get; set; }
    }

    // Commands
    public record ProcessBatchItemCommand(string BatchId, string ItemId) : IRequest<bool>;
    public record CheckBatchProgressCommand(string BatchId) : IRequest<BatchProgress>;
    public record ExecuteSagaStep1Command(string TransactionId, decimal Amount) : IRequest<bool>;
    public record ExecuteSagaStep2Command(string TransactionId) : IRequest<bool>;
    public record ExecuteSagaStep3Command(string TransactionId) : IRequest<bool>;
    public record CompensateSagaCommand(string TransactionId) : IRequest;

    public record BatchProgress(int Processed, int Total, bool IsComplete);

    #endregion

    #region Scenario 1: Batch Processing with Progress Checks

    /// <summary>
    /// Scenario: Start batch -> Check progress every 30s -> Complete when done
    /// </summary>
    [Fact]
    public async Task BatchProcessing_ShouldScheduleProgressChecks()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateInMemoryStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        var scheduleCount = 0;
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromResult($"progress-check-{++scheduleCount}"));

        mediator.SendAsync(Arg.Any<CheckBatchProgressCommand>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult<BatchProgress>.Success(new BatchProgress(50, 100, false)));

        var config = new BatchProcessingFlowConfig();
        config.Build();

        var executor = new DslFlowExecutor<BatchProcessState, BatchProcessingFlowConfig>(mediator, store, config, scheduler);
        var state = new BatchProcessState
        {
            Items = Enumerable.Range(1, 100).Select(i => $"item-{i}").ToList()
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Status.Should().Be(DslFlowStatus.Suspended);
        scheduleCount.Should().Be(1);
    }

    private class BatchProcessingFlowConfig : FlowConfig<BatchProcessState>
    {
        public override string FlowId => "batch-processing";

        protected override void Configure(IFlowBuilder<BatchProcessState> flow)
        {
            flow
                .Send<CheckBatchProgressCommand, BatchProgress>(s => new CheckBatchProgressCommand(s.BatchId))
                .If(s => !s.IsCompleted)
                    .Delay(TimeSpan.FromSeconds(30)) // Check again in 30s
                .EndIf();
        }
    }

    #endregion

    #region Scenario 2: Saga with Timeout

    /// <summary>
    /// Scenario: Multi-step saga with timeout - compensate if not completed in 5 minutes
    /// </summary>
    [Fact]
    public async Task SagaWithTimeout_ShouldScheduleTimeoutCheck()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateInMemoryStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        DateTimeOffset scheduledTime = default;
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                scheduledTime = callInfo.ArgAt<DateTimeOffset>(2);
                return ValueTask.FromResult("saga-timeout");
            });

        mediator.SendAsync(Arg.Any<ExecuteSagaStep1Command>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult<bool>.Success(true));

        var config = new SagaWithTimeoutFlowConfig();
        config.Build();

        var executor = new DslFlowExecutor<SagaState, SagaWithTimeoutFlowConfig>(mediator, store, config, scheduler);
        var state = new SagaState { Amount = 1000m };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Status.Should().Be(DslFlowStatus.Suspended);
        var delay = scheduledTime - DateTimeOffset.UtcNow;
        delay.Should().BeCloseTo(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(10));
    }

    private class SagaWithTimeoutFlowConfig : FlowConfig<SagaState>
    {
        public override string FlowId => "saga-timeout";

        protected override void Configure(IFlowBuilder<SagaState> flow)
        {
            flow
                .Send<ExecuteSagaStep1Command, bool>(s => new ExecuteSagaStep1Command(s.TransactionId, s.Amount))
                .Delay(TimeSpan.FromMinutes(5)) // Timeout for saga completion
                .If(s => !s.Step3Completed)
                    .Send(s => new CompensateSagaCommand(s.TransactionId))
                .EndIf();
        }
    }

    #endregion

    #region Scenario 3: Rate-Limited API Calls

    /// <summary>
    /// Scenario: Call API with rate limiting - 10 calls per minute
    /// </summary>
    [Fact]
    public async Task RateLimitedAPICalls_ShouldDelayBetweenCalls()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateInMemoryStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        var delays = new List<TimeSpan>();
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                delays.Add(callInfo.ArgAt<DateTimeOffset>(2) - DateTimeOffset.UtcNow);
                return ValueTask.FromResult($"rate-limit-{delays.Count}");
            });

        mediator.SendAsync(Arg.Any<ProcessBatchItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult<bool>.Success(true));

        var config = new RateLimitedAPIFlowConfig();
        config.Build();

        var executor = new DslFlowExecutor<BatchProcessState, RateLimitedAPIFlowConfig>(mediator, store, config, scheduler);
        var state = new BatchProcessState
        {
            Items = ["item-1", "item-2", "item-3"]
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Status.Should().Be(DslFlowStatus.Suspended);
        delays.Should().HaveCount(1);
        delays[0].Should().BeCloseTo(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(1)); // 60s / 10 calls = 6s between calls
    }

    private class RateLimitedAPIFlowConfig : FlowConfig<BatchProcessState>
    {
        public override string FlowId => "rate-limited-api";

        protected override void Configure(IFlowBuilder<BatchProcessState> flow)
        {
            flow
                .Send<ProcessBatchItemCommand, bool>(s => new ProcessBatchItemCommand(s.BatchId, s.Items.FirstOrDefault() ?? ""))
                .Delay(TimeSpan.FromSeconds(6)); // Rate limit: 10 calls per minute
        }
    }

    #endregion

    #region Scenario 4: Parallel Flow Coordination

    /// <summary>
    /// Scenario: Multiple flows running in parallel with coordinated completion
    /// </summary>
    [Fact]
    public async Task ParallelFlows_ShouldScheduleIndependently()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateInMemoryStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        var scheduledFlows = new List<string>();
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var flowId = callInfo.ArgAt<string>(0);
                scheduledFlows.Add(flowId);
                return ValueTask.FromResult($"schedule-{flowId}");
            });

        var config = new ParallelFlowConfig();
        config.Build();

        // Run 3 flows in parallel
        var tasks = Enumerable.Range(1, 3).Select(async i =>
        {
            var executor = new DslFlowExecutor<BatchProcessState, ParallelFlowConfig>(mediator, store, config, scheduler);
            var state = new BatchProcessState { BatchId = $"batch-{i}" };
            return await executor.RunAsync(state);
        });

        // Act
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(r => r.Status.Should().Be(DslFlowStatus.Suspended));
        scheduledFlows.Should().HaveCount(3);
        scheduledFlows.Should().OnlyHaveUniqueItems();
    }

    private class ParallelFlowConfig : FlowConfig<BatchProcessState>
    {
        public override string FlowId => "parallel-flow";

        protected override void Configure(IFlowBuilder<BatchProcessState> flow)
        {
            flow
                .Delay(TimeSpan.FromMinutes(1)); // Each flow waits independently
        }
    }

    #endregion

    #region Scenario 5: Workflow Orchestration with Dependencies

    /// <summary>
    /// Scenario: Orchestrate multiple dependent workflows with scheduled handoffs
    /// </summary>
    [Fact]
    public async Task WorkflowOrchestration_ShouldScheduleDependentSteps()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateInMemoryStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        var scheduleCount = 0;
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromResult($"orchestrate-{++scheduleCount}"));

        mediator.SendAsync(Arg.Any<ExecuteSagaStep1Command>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult<bool>.Success(true));

        var config = new OrchestrationFlowConfig();
        config.Build();

        var executor = new DslFlowExecutor<SagaState, OrchestrationFlowConfig>(mediator, store, config, scheduler);
        var state = new SagaState { Amount = 500m };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Status.Should().Be(DslFlowStatus.Suspended);
    }

    private class OrchestrationFlowConfig : FlowConfig<SagaState>
    {
        public override string FlowId => "orchestration";

        protected override void Configure(IFlowBuilder<SagaState> flow)
        {
            flow
                .Send<ExecuteSagaStep1Command, bool>(s => new ExecuteSagaStep1Command(s.TransactionId, s.Amount))
                .Delay(TimeSpan.FromSeconds(10)) // Wait for external system
                .Send<ExecuteSagaStep2Command, bool>(s => new ExecuteSagaStep2Command(s.TransactionId))
                .Delay(TimeSpan.FromSeconds(10)) // Wait for confirmation
                .Send<ExecuteSagaStep3Command, bool>(s => new ExecuteSagaStep3Command(s.TransactionId));
        }
    }

    #endregion

    #region Helper

    private static InMemoryTestStore CreateInMemoryStore() => new();

    private class InMemoryTestStore : IDslFlowStore
    {
        private readonly Dictionary<string, object> _flows = new();
        private readonly Dictionary<string, WaitCondition> _waitConditions = new();
        private readonly Dictionary<string, ForEachProgress> _forEachProgress = new();

        public Task<bool> CreateAsync<TState>(FlowSnapshot<TState> snapshot, CancellationToken ct = default) where TState : class, IFlowState
        { _flows[snapshot.FlowId] = snapshot; return Task.FromResult(true); }
        public Task<FlowSnapshot<TState>?> GetAsync<TState>(string flowId, CancellationToken ct = default) where TState : class, IFlowState
            => Task.FromResult(_flows.TryGetValue(flowId, out var s) ? (FlowSnapshot<TState>?)s : null);
        public Task<bool> UpdateAsync<TState>(FlowSnapshot<TState> snapshot, CancellationToken ct = default) where TState : class, IFlowState
        { _flows[snapshot.FlowId] = snapshot; return Task.FromResult(true); }
        public Task<bool> DeleteAsync(string flowId, CancellationToken ct = default) => Task.FromResult(_flows.Remove(flowId));
        public Task SetWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default)
        { _waitConditions[correlationId] = condition; return Task.CompletedTask; }
        public Task<WaitCondition?> GetWaitConditionAsync(string correlationId, CancellationToken ct = default)
            => Task.FromResult(_waitConditions.TryGetValue(correlationId, out var c) ? c : null);
        public Task UpdateWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default)
        { _waitConditions[correlationId] = condition; return Task.CompletedTask; }
        public Task ClearWaitConditionAsync(string correlationId, CancellationToken ct = default)
        { _waitConditions.Remove(correlationId); return Task.CompletedTask; }
        public Task<IReadOnlyList<WaitCondition>> GetTimedOutWaitConditionsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WaitCondition>>(_waitConditions.Values.Where(c => DateTime.UtcNow - c.CreatedAt > c.Timeout).ToList());
        public Task SaveForEachProgressAsync(string flowId, int stepIndex, ForEachProgress progress, CancellationToken ct = default)
        { _forEachProgress[$"{flowId}:{stepIndex}"] = progress; return Task.CompletedTask; }
        public Task<ForEachProgress?> GetForEachProgressAsync(string flowId, int stepIndex, CancellationToken ct = default)
            => Task.FromResult(_forEachProgress.TryGetValue($"{flowId}:{stepIndex}", out var p) ? p : null);
        public Task ClearForEachProgressAsync(string flowId, int stepIndex, CancellationToken ct = default)
        { _forEachProgress.Remove($"{flowId}:{stepIndex}"); return Task.CompletedTask; }
    }

    #endregion
}
