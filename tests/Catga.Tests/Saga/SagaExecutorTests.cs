using Catga.Saga;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Catga.Tests.Saga;

public class SagaExecutorTests
{
    private readonly SagaExecutor _executor;

    public SagaExecutorTests()
    {
        _executor = new SagaExecutor(NullLogger<SagaExecutor>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_AllStepsSucceed_ReturnsSucceeded()
    {
        // Arrange
        var step1Executed = false;
        var step2Executed = false;

        var saga = SagaBuilder.Create()
            .AddStep("Step1",
                execute: async _ => { step1Executed = true; await Task.CompletedTask; },
                compensate: async _ => await Task.CompletedTask)
            .AddStep("Step2",
                execute: async _ => { step2Executed = true; await Task.CompletedTask; },
                compensate: async _ => await Task.CompletedTask)
            .Build();

        // Act
        var result = await _executor.ExecuteAsync(saga);

        // Assert
        Assert.Equal(SagaStatus.Succeeded, result.Status);
        Assert.Equal(2, result.StepsExecuted);
        Assert.True(step1Executed);
        Assert.True(step2Executed);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_StepFails_CompensatesExecutedSteps()
    {
        // Arrange
        var step1Executed = false;
        var step1Compensated = false;
        var step2Executed = false;

        var saga = SagaBuilder.Create()
            .AddStep("Step1",
                execute: async _ => { step1Executed = true; await Task.CompletedTask; },
                compensate: async _ => { step1Compensated = true; await Task.CompletedTask; })
            .AddStep("Step2",
                execute: async _ => { step2Executed = true; throw new InvalidOperationException("Step 2 failed"); },
                compensate: async _ => await Task.CompletedTask)
            .Build();

        // Act
        var result = await _executor.ExecuteAsync(saga);

        // Assert
        Assert.Equal(SagaStatus.Compensated, result.Status);
        Assert.Equal(2, result.StepsExecuted);
        Assert.True(step1Executed);
        Assert.True(step2Executed);
        Assert.True(step1Compensated);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Step 2 failed", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_FirstStepFails_NoCompensation()
    {
        // Arrange
        var step1Executed = false;
        var step1Compensated = false;

        var saga = SagaBuilder.Create()
            .AddStep("Step1",
                execute: async _ => { step1Executed = true; throw new InvalidOperationException("Step 1 failed"); },
                compensate: async _ => { step1Compensated = true; await Task.CompletedTask; })
            .Build();

        // Act
        var result = await _executor.ExecuteAsync(saga);

        // Assert
        Assert.Equal(SagaStatus.Compensated, result.Status);
        Assert.Equal(1, result.StepsExecuted);
        Assert.True(step1Executed);
        Assert.True(step1Compensated);
    }

    [Fact]
    public async Task ExecuteAsync_EmptySaga_ReturnsSucceeded()
    {
        // Arrange
        var saga = SagaBuilder.Create().Build();

        // Act
        var result = await _executor.ExecuteAsync(saga);

        // Assert
        Assert.Equal(SagaStatus.Succeeded, result.Status);
        Assert.Equal(0, result.StepsExecuted);
    }

    [Fact]
    public async Task ExecuteAsync_CompensationInReverseOrder()
    {
        // Arrange
        var compensationOrder = new List<int>();

        var saga = SagaBuilder.Create()
            .AddStep("Step1",
                execute: async _ => await Task.CompletedTask,
                compensate: async _ => { compensationOrder.Add(1); await Task.CompletedTask; })
            .AddStep("Step2",
                execute: async _ => await Task.CompletedTask,
                compensate: async _ => { compensationOrder.Add(2); await Task.CompletedTask; })
            .AddStep("Step3",
                execute: async _ => throw new InvalidOperationException("Step 3 failed"),
                compensate: async _ => { compensationOrder.Add(3); await Task.CompletedTask; })
            .Build();

        // Act
        var result = await _executor.ExecuteAsync(saga);

        // Assert
        Assert.Equal(SagaStatus.Compensated, result.Status);
        Assert.Equal(new[] { 3, 2, 1 }, compensationOrder);
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var saga = SagaBuilder.Create()
            .AddStep("Step1",
                execute: async ct =>
                {
                    cts.Cancel();
                    ct.ThrowIfCancellationRequested();
                    await Task.CompletedTask;
                },
                compensate: async _ => await Task.CompletedTask)
            .Build();

        // Act
        var result = await _executor.ExecuteAsync(saga, cts.Token);

        // Assert
        Assert.Equal(SagaStatus.Compensated, result.Status);
        Assert.Contains("canceled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SagaBuilder_CreatesSagaWithCorrectSteps()
    {
        // Arrange & Act
        var saga = SagaBuilder.Create("test-saga")
            .AddStep("Step1",
                execute: async _ => await Task.CompletedTask,
                compensate: async _ => await Task.CompletedTask)
            .AddStep("Step2",
                execute: async _ => await Task.CompletedTask,
                compensate: async _ => await Task.CompletedTask)
            .Build();

        // Assert
        Assert.Equal("test-saga", saga.SagaId);
        Assert.Equal(2, saga.Steps.Count);
        Assert.Equal("Step1", saga.Steps[0].Name);
        Assert.Equal("Step2", saga.Steps[1].Name);
    }
}

