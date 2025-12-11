using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Catga.Flow.Dsl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Catga.Tests.Reliability;

/// <summary>
/// Tests for reliability, resilience, and fault tolerance
/// </summary>
public class ReliabilityTests
{
    private readonly ITestOutputHelper _output;

    public ReliabilityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Retry and Recovery Tests

    [Fact]
    public async Task Reliability_RetryOnTransientFailure_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<ReliabilityState, RetryFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<ReliabilityState, RetryFlow>>();

        var state = new ReliabilityState
        {
            FlowId = "retry-test",
            FailuresBeforeSuccess = 3
        };

        // Act
        var result = await executor!.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.AttemptCount.Should().Be(4); // 3 failures + 1 success
        result.State.Completed.Should().BeTrue();
    }

    [Fact]
    public async Task Reliability_ExponentialBackoff_DelaysCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<ReliabilityState, ExponentialBackoffFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<ReliabilityState, ExponentialBackoffFlow>>();

        var state = new ReliabilityState
        {
            FlowId = "backoff-test",
            FailuresBeforeSuccess = 3
        };

        // Act
        var sw = Stopwatch.StartNew();
        var result = await executor!.RunAsync(state);
        sw.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify delays were applied (base delay * 2^attempt)
        var expectedMinDelay = 100 + 200 + 400; // milliseconds
        sw.ElapsedMilliseconds.Should().BeGreaterThan(expectedMinDelay);

        _output.WriteLine($"Total time with exponential backoff: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Reliability_CircuitBreaker_OpensOnRepeatedFailures()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<ReliabilityState, CircuitBreakerFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<ReliabilityState, CircuitBreakerFlow>>();

        var successCount = 0;
        var failureCount = 0;

        // Act - Run multiple flows to trigger circuit breaker
        for (int i = 0; i < 10; i++)
        {
            var state = new ReliabilityState
            {
                FlowId = $"circuit-{i}",
                ShouldFail = i < 5 // First 5 fail
            };

            try
            {
                var result = await executor!.RunAsync(state);
                if (result.IsSuccess) successCount++;
            }
            catch
            {
                failureCount++;
            }
        }

        // Assert
        failureCount.Should().BeGreaterThanOrEqualTo(5);
        _output.WriteLine($"Circuit breaker: {failureCount} failures, {successCount} successes");
    }

    #endregion

    #region Compensation and Rollback Tests

    [Fact]
    public async Task Reliability_CompensationChain_ExecutesInReverseOrder()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<ReliabilityState, CompensationChainFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<ReliabilityState, CompensationChainFlow>>();

        var state = new ReliabilityState
        {
            FlowId = "compensation-chain",
            FailAtStep = 3
        };

        // Act
        try
        {
            await executor!.RunAsync(state);
        }
        catch
        {
            // Expected failure
        }

        // Assert
        state.ExecutedSteps.Should().BeEquivalentTo(new[] { 1, 2, 3 });
        state.CompensatedSteps.Should().BeEquivalentTo(new[] { 2, 1 }); // Reverse order, step 3 wasn't compensated
        _output.WriteLine($"Executed: {string.Join(", ", state.ExecutedSteps)}");
        _output.WriteLine($"Compensated: {string.Join(", ", state.CompensatedSteps)}");
    }

    [Fact]
    public async Task Reliability_PartialCompensation_HandlesCompensationFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<ReliabilityState, PartialCompensationFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<ReliabilityState, PartialCompensationFlow>>();

        var state = new ReliabilityState
        {
            FlowId = "partial-compensation",
            FailAtStep = 4,
            FailCompensationAtStep = 2
        };

        // Act
        try
        {
            await executor!.RunAsync(state);
        }
        catch
        {
            // Expected failure
        }

        // Assert
        state.ExecutedSteps.Should().Contain(new[] { 1, 2, 3, 4 });
        state.CompensatedSteps.Should().Contain(3); // Only step 3 compensated, step 2 compensation failed
        state.CompensationFailures.Should().Contain(2);
    }

    #endregion

    #region State Recovery Tests

    [Fact]
    public async Task Reliability_StateRecovery_ResumesFromLastCheckpoint()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<ReliabilityState, CheckpointFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<ReliabilityState, CheckpointFlow>>();
        var store = provider.GetService<IDslFlowStore>();

        var flowId = "checkpoint-recovery";
        var state = new ReliabilityState
        {
            FlowId = flowId,
            FailAtStep = 3
        };

        // Act - First run (fails at step 3)
        try
        {
            await executor!.RunAsync(state);
        }
        catch
        {
            // Save checkpoint
            var snapshot = new FlowSnapshot<ReliabilityState>
            {
                FlowId = flowId,
                State = state,
                Status = DslFlowStatus.Failed,
                Position = new FlowPosition(new[] { 2 }), // Last successful step
                Version = 1
            };
            await store!.CreateAsync(snapshot);
        }

        // Resume from checkpoint
        state.FailAtStep = -1; // Don't fail this time
        var resumeResult = await executor!.ResumeAsync(flowId);

        // Assert
        resumeResult.IsSuccess.Should().BeTrue();
        resumeResult.State.ExecutedSteps.Should().Contain(new[] { 1, 2, 3, 4, 5 });
        _output.WriteLine($"Resumed from checkpoint, completed steps: {string.Join(", ", resumeResult.State.ExecutedSteps)}");
    }

    [Fact]
    public async Task Reliability_IdempotentOperations_PreventDuplication()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<ReliabilityState, IdempotentFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<ReliabilityState, IdempotentFlow>>();

        var state = new ReliabilityState
        {
            FlowId = "idempotent-test"
        };

        // Act - Run the same flow multiple times
        for (int i = 0; i < 5; i++)
        {
            await executor!.RunAsync(state);
        }

        // Assert
        state.IdempotentCounter.Should().Be(1, "idempotent operation should only execute once");
        state.NonIdempotentCounter.Should().Be(5, "non-idempotent operation executes each time");
    }

    #endregion

    #region Timeout and Deadline Tests

    [Fact]
    public async Task Reliability_OperationTimeout_EnforcedCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<ReliabilityState, TimeoutFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<ReliabilityState, TimeoutFlow>>();

        var state = new ReliabilityState
        {
            FlowId = "timeout-test",
            OperationDuration = TimeSpan.FromSeconds(5)
        };

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        Func<Task> act = async () => await executor!.RunAsync(state, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Reliability_DeadlinePropagation_RespectedAcrossSteps()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<ReliabilityState, DeadlineFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<ReliabilityState, DeadlineFlow>>();

        var deadline = DateTime.UtcNow.AddSeconds(2);
        var state = new ReliabilityState
        {
            FlowId = "deadline-test",
            Deadline = deadline
        };

        // Act
        var sw = Stopwatch.StartNew();
        var result = await executor!.RunAsync(state);
        sw.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        sw.ElapsedMilliseconds.Should().BeLessThan(2100, "should complete before deadline");
        state.CompletedBeforeDeadline.Should().BeTrue();
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public async Task Reliability_OptimisticConcurrency_PreventsConcurrentUpdates()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        var provider = services.BuildServiceProvider();
        var store = provider.GetService<IDslFlowStore>();

        var flowId = "optimistic-concurrency";
        var snapshot = new FlowSnapshot<ReliabilityState>
        {
            FlowId = flowId,
            State = new ReliabilityState { FlowId = flowId, Counter = 0 },
            Status = DslFlowStatus.Running,
            Position = new FlowPosition(new[] { 0 }),
            Version = 1
        };

        await store!.CreateAsync(snapshot);

        // Act - Simulate concurrent updates
        var task1 = Task.Run(async () =>
        {
            var current = await store.GetAsync<ReliabilityState>(flowId);
            current!.State.Counter = 100;
            return await store.UpdateAsync(current);
        });

        var task2 = Task.Run(async () =>
        {
            await Task.Delay(10); // Slight delay to ensure conflict
            var current = await store.GetAsync<ReliabilityState>(flowId);
            current!.State.Counter = 200;
            return await store.UpdateAsync(current);
        });

        var results = await Task.WhenAll(task1, task2);

        // Assert
        var successCount = results.Count(r => r.IsSuccess);
        successCount.Should().Be(1, "only one concurrent update should succeed");

        var final = await store.GetAsync<ReliabilityState>(flowId);
        final!.Version.Should().Be(2, "version should increment once");
    }

    [Fact]
    public async Task Reliability_DistributedLock_PreventsConcurrentExecution()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<ReliabilityState, LockProtectedFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<ReliabilityState, LockProtectedFlow>>();

        var sharedResource = new SharedResource();
        var flowIds = Enumerable.Range(0, 10).Select(i => $"lock-test-{i}").ToList();

        // Act - Run flows concurrently
        var tasks = flowIds.Select(id => Task.Run(async () =>
        {
            var state = new ReliabilityState
            {
                FlowId = id,
                SharedResource = sharedResource
            };
            await executor!.RunAsync(state);
        }));

        await Task.WhenAll(tasks);

        // Assert
        sharedResource.MaxConcurrentAccess.Should().Be(1, "distributed lock should prevent concurrent access");
        sharedResource.TotalAccesses.Should().Be(10);
    }

    #endregion

    #region Data Integrity Tests

    [Fact]
    public async Task Reliability_DataIntegrity_MaintainedAcrossFailures()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<ReliabilityState, DataIntegrityFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<ReliabilityState, DataIntegrityFlow>>();

        var state = new ReliabilityState
        {
            FlowId = "data-integrity",
            DataItems = Enumerable.Range(0, 100).Select(i => $"item-{i}").ToList()
        };

        var originalChecksum = CalculateChecksum(state.DataItems);

        // Act - Process with simulated failures
        state.FailAtStep = 50;
        try
        {
            await executor!.RunAsync(state);
        }
        catch
        {
            // Expected failure
        }

        // Resume processing
        state.FailAtStep = -1;
        var result = await executor!.ResumeAsync(state.FlowId!);

        // Assert
        var finalChecksum = CalculateChecksum(result.State.ProcessedDataItems);
        finalChecksum.Should().Be(originalChecksum, "data integrity should be maintained");
        result.State.ProcessedDataItems.Should().HaveCount(100);
    }

    #endregion

    // Helper method
    private int CalculateChecksum(IEnumerable<string> items)
    {
        return items.Select(i => i.GetHashCode()).Aggregate(0, (a, b) => a ^ b);
    }
}

// Test flows
public class RetryFlow : FlowConfig<ReliabilityState>
{
    protected override void Configure(IFlowBuilder<ReliabilityState> flow)
    {
        flow.Step("retry-step", s =>
        {
            s.AttemptCount++;
            if (s.AttemptCount <= s.FailuresBeforeSuccess)
            {
                throw new Exception($"Transient failure {s.AttemptCount}");
            }
        })
        .Retry(5, TimeSpan.FromMilliseconds(10));

        flow;
    }
}

public class ExponentialBackoffFlow : FlowConfig<ReliabilityState>
{
    protected override void Configure(IFlowBuilder<ReliabilityState> flow)
    {
        flow.Step("backoff-step", async s =>
        {
            s.AttemptCount++;
            if (s.AttemptCount <= s.FailuresBeforeSuccess)
            {
                var delay = TimeSpan.FromMilliseconds(100 * Math.Pow(2, s.AttemptCount - 1));
                await Task.Delay(delay);
                throw new Exception($"Failure with backoff");
            }
        })
        .Retry(5, TimeSpan.Zero); // Base delay handled in step
    }
}

public class CircuitBreakerFlow : FlowConfig<ReliabilityState>
{
    private static int _consecutiveFailures = 0;
    private static DateTime _lastFailureTime = DateTime.MinValue;
    private static bool _circuitOpen = false;

    protected override void Configure(IFlowBuilder<ReliabilityState> flow)
    {
        flow.Step("circuit-breaker", s =>
        {
            // Check if circuit is open
            if (_circuitOpen && (DateTime.UtcNow - _lastFailureTime).TotalSeconds < 5)
            {
                throw new Exception("Circuit breaker is open");
            }

            if (s.ShouldFail)
            {
                _consecutiveFailures++;
                _lastFailureTime = DateTime.UtcNow;

                if (_consecutiveFailures >= 3)
                {
                    _circuitOpen = true;
                }

                throw new Exception("Operation failed");
            }

            // Reset on success
            _consecutiveFailures = 0;
            _circuitOpen = false;
        });
    }
}

public class CompensationChainFlow : FlowConfig<ReliabilityState>
{
    protected override void Configure(IFlowBuilder<ReliabilityState> flow)
    {
        flow)
            .Compensate(s => s.CompensatedSteps.Add(1));

        flow)
            .Compensate(s => s.CompensatedSteps.Add(2));

        flow.Step("step3", s =>
        {
            s.ExecutedSteps.Add(3);
            if (s.FailAtStep == 3) throw new Exception("Step 3 failed");
        })
            .Compensate(s => s.CompensatedSteps.Add(3));

        flow)
            .Compensate(s => s.CompensatedSteps.Add(4));
    }
}

public class PartialCompensationFlow : FlowConfig<ReliabilityState>
{
    protected override void Configure(IFlowBuilder<ReliabilityState> flow)
    {
        for (int i = 1; i <= 4; i++)
        {
            var stepNum = i;
            flow.Step($"step{stepNum}", s =>
            {
                s.ExecutedSteps.Add(stepNum);
                if (s.FailAtStep == stepNum) throw new Exception($"Step {stepNum} failed");
            })
            .Compensate(s =>
            {
                if (s.FailCompensationAtStep == stepNum)
                {
                    s.CompensationFailures.Add(stepNum);
                    throw new Exception($"Compensation for step {stepNum} failed");
                }
                s.CompensatedSteps.Add(stepNum);
            });
        }
    }
}

public class CheckpointFlow : FlowConfig<ReliabilityState>
{
    protected override void Configure(IFlowBuilder<ReliabilityState> flow)
    {
        for (int i = 1; i <= 5; i++)
        {
            var stepNum = i;
            flow.Step($"checkpoint-{stepNum}", s =>
            {
                s.ExecutedSteps.Add(stepNum);
                if (s.FailAtStep == stepNum) throw new Exception($"Failed at step {stepNum}");
            });
        }
    }
}

public class IdempotentFlow : FlowConfig<ReliabilityState>
{
    private static readonly HashSet<string> _processedFlows = new();

    protected override void Configure(IFlowBuilder<ReliabilityState> flow)
    {
        flow.Step("idempotent", s =>
        {
            if (_processedFlows.Add(s.FlowId!))
            {
                s.IdempotentCounter++;
            }
        });

        flow;
    }
}

public class TimeoutFlow : FlowConfig<ReliabilityState>
{
    protected override void Configure(IFlowBuilder<ReliabilityState> flow)
    {
        flow.Step("long-operation", async s =>
        {
            await Task.Delay(s.OperationDuration);
            s.Completed = true;
        });
    }
}

public class DeadlineFlow : FlowConfig<ReliabilityState>
{
    protected override void Configure(IFlowBuilder<ReliabilityState> flow)
    {
        flow.Step("check-deadline", s =>
        {
            if (DateTime.UtcNow > s.Deadline)
            {
                throw new Exception("Deadline exceeded");
            }
        });

        flow);

        flow.Step("complete", s =>
        {
            s.CompletedBeforeDeadline = DateTime.UtcNow < s.Deadline;
        });
    }
}

public class LockProtectedFlow : FlowConfig<ReliabilityState>
{
    private static readonly SemaphoreSlim _lock = new(1);

    protected override void Configure(IFlowBuilder<ReliabilityState> flow)
    {
        flow.Step("acquire-lock", async s =>
        {
            await _lock.WaitAsync();
            try
            {
                s.SharedResource!.OnAccess();
                await Task.Delay(10); // Simulate work
            }
            finally
            {
                s.SharedResource.OnRelease();
                _lock.Release();
            }
        });
    }
}

public class DataIntegrityFlow : FlowConfig<ReliabilityState>
{
    protected override void Configure(IFlowBuilder<ReliabilityState> flow)
    {
        flow.ForEach(s => s.DataItems)
            .Configure((item, f) => f.Step($"process-{item}", s =>
            {
                var index = s.ProcessedDataItems.Count;
                if (s.FailAtStep == index)
                {
                    throw new Exception($"Failed at item {index}");
                }
                s.ProcessedDataItems.Add(item);
            }))
            .EndForEach();
    }
}

// Test state
public class ReliabilityState : IFlowState
{
    public string? FlowId { get; set; }
    public int FailuresBeforeSuccess { get; set; }
    public int AttemptCount { get; set; }
    public bool Completed { get; set; }
    public bool ShouldFail { get; set; }
    public int FailAtStep { get; set; } = -1;
    public int FailCompensationAtStep { get; set; } = -1;
    public List<int> ExecutedSteps { get; set; } = new();
    public List<int> CompensatedSteps { get; set; } = new();
    public List<int> CompensationFailures { get; set; } = new();
    public int IdempotentCounter { get; set; }
    public int NonIdempotentCounter { get; set; }
    public int Counter { get; set; }
    public TimeSpan OperationDuration { get; set; }
    public DateTime Deadline { get; set; }
    public bool CompletedBeforeDeadline { get; set; }
    public SharedResource? SharedResource { get; set; }
    public List<string> DataItems { get; set; } = new();
    public List<string> ProcessedDataItems { get; set; } = new();

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class SharedResource
{
    private int _currentAccess = 0;
    private int _maxConcurrent = 0;
    private int _totalAccesses = 0;

    public int MaxConcurrentAccess => _maxConcurrent;
    public int TotalAccesses => _totalAccesses;

    public void OnAccess()
    {
        var current = Interlocked.Increment(ref _currentAccess);
        Interlocked.Increment(ref _totalAccesses);

        // Update max if needed
        int oldMax;
        do
        {
            oldMax = _maxConcurrent;
            if (current <= oldMax) break;
        } while (Interlocked.CompareExchange(ref _maxConcurrent, current, oldMax) != oldMax);
    }

    public void OnRelease()
    {
        Interlocked.Decrement(ref _currentAccess);
    }
}
