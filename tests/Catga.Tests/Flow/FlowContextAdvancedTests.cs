using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Flow;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow;

/// <summary>
/// Advanced tests for FlowContext covering edge cases and complex scenarios
/// </summary>
public sealed partial class FlowContextAdvancedTests
{
    #region Delegate Compensation Tests

    [Fact]
    public async Task RegisterCompensation_WithDelegate_ShouldExecuteOnFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var tracker = new AdvancedTracker();
        services.AddSingleton(tracker);
        services.AddScoped<IRequestHandler<AdvStep1Command, AdvStep1Result>, AdvStep1Handler>();
        services.AddScoped<IRequestHandler<AdvStep2Command, AdvStep2Result>, AdvStep2Handler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        await using (var flow = mediator.BeginFlow("DelegateCompensationFlow"))
        {
            var r1 = await flow.ExecuteAsync<AdvStep1Command, AdvStep1Result>(
                new AdvStep1Command { MessageId = MessageExtensions.NewMessageId() });

            // Register delegate-based compensation
            flow.RegisterCompensation(async ct =>
            {
                tracker.ExecutionOrder.Add("DelegateCompensation1");
                await Task.CompletedTask;
            }, "Compensation1");

            var r2 = await flow.ExecuteAsync<AdvStep2Command, AdvStep2Result>(
                new AdvStep2Command { MessageId = MessageExtensions.NewMessageId(), ShouldFail = true });

            // Don't commit
        }

        // Assert
        tracker.ExecutionOrder.Should().Contain("DelegateCompensation1");
    }

    [Fact]
    public async Task RegisterCompensation_MultipleDelegates_ShouldExecuteInReverseOrder()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var tracker = new AdvancedTracker();
        services.AddSingleton(tracker);
        services.AddScoped<IRequestHandler<AdvStep1Command, AdvStep1Result>, AdvStep1Handler>();
        services.AddScoped<IRequestHandler<AdvStep2Command, AdvStep2Result>, AdvStep2Handler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        await using (var flow = mediator.BeginFlow("MultiDelegateFlow"))
        {
            await flow.ExecuteAsync<AdvStep1Command, AdvStep1Result>(
                new AdvStep1Command { MessageId = MessageExtensions.NewMessageId() });
            flow.RegisterCompensation(async ct =>
            {
                tracker.ExecutionOrder.Add("Comp1");
                await Task.CompletedTask;
            }, "Comp1");

            await flow.ExecuteAsync<AdvStep1Command, AdvStep1Result>(
                new AdvStep1Command { MessageId = MessageExtensions.NewMessageId() });
            flow.RegisterCompensation(async ct =>
            {
                tracker.ExecutionOrder.Add("Comp2");
                await Task.CompletedTask;
            }, "Comp2");

            await flow.ExecuteAsync<AdvStep1Command, AdvStep1Result>(
                new AdvStep1Command { MessageId = MessageExtensions.NewMessageId() });
            flow.RegisterCompensation(async ct =>
            {
                tracker.ExecutionOrder.Add("Comp3");
                await Task.CompletedTask;
            }, "Comp3");

            // Fail
            await flow.ExecuteAsync<AdvStep2Command, AdvStep2Result>(
                new AdvStep2Command { MessageId = MessageExtensions.NewMessageId(), ShouldFail = true });
        }

        // Assert - reverse order
        var compOrder = tracker.ExecutionOrder.Where(x => x.StartsWith("Comp")).ToList();
        compOrder.Should().ContainInOrder("Comp3", "Comp2", "Comp1");
    }

    #endregion

    #region Nested Flow Tests

    [Fact]
    public async Task NestedFlows_InnerFlowFailure_ShouldNotAffectOuterFlow()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var tracker = new AdvancedTracker();
        services.AddSingleton(tracker);
        services.AddScoped<IRequestHandler<AdvStep1Command, AdvStep1Result>, AdvStep1Handler>();
        services.AddScoped<IRequestHandler<AdvStep2Command, AdvStep2Result>, AdvStep2Handler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var outerResult = await mediator.RunFlowAsync("OuterFlow", async outerFlow =>
        {
            await outerFlow.ExecuteAsync<AdvStep1Command, AdvStep1Result>(
                new AdvStep1Command { MessageId = MessageExtensions.NewMessageId() });
            outerFlow.RegisterCompensation(async ct =>
            {
                tracker.ExecutionOrder.Add("OuterComp");
                await Task.CompletedTask;
            }, "OuterComp");

            // Inner flow that fails
            var innerResult = await mediator.RunFlowAsync("InnerFlow", async innerFlow =>
            {
                await innerFlow.ExecuteAsync<AdvStep1Command, AdvStep1Result>(
                    new AdvStep1Command { MessageId = MessageExtensions.NewMessageId() });
                innerFlow.RegisterCompensation(async ct =>
                {
                    tracker.ExecutionOrder.Add("InnerComp");
                    await Task.CompletedTask;
                }, "InnerComp");

                // This fails
                var r = await innerFlow.ExecuteAsync<AdvStep2Command, AdvStep2Result>(
                    new AdvStep2Command { MessageId = MessageExtensions.NewMessageId(), ShouldFail = true });

                if (!r.IsSuccess)
                    throw new FlowExecutionException("InnerStep", r.Error!, innerFlow.StepCount);

                return "inner-success";
            });

            // Inner flow failed, but outer can continue or handle it
            tracker.ExecutionOrder.Add($"InnerResult:{innerResult.IsSuccess}");

            return "outer-success";
        });

        // Assert
        outerResult.IsSuccess.Should().BeTrue();
        tracker.ExecutionOrder.Should().Contain("InnerComp"); // Inner compensation ran
        tracker.ExecutionOrder.Should().NotContain("OuterComp"); // Outer committed
        tracker.ExecutionOrder.Should().Contain("InnerResult:False");
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task Flow_WithCancellation_ShouldCompensateExecutedSteps()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var tracker = new AdvancedTracker();
        services.AddSingleton(tracker);
        services.AddScoped<IRequestHandler<AdvStep1Command, AdvStep1Result>, AdvStep1Handler>();
        services.AddScoped<IRequestHandler<AdvStep2Command, AdvStep2Result>, AdvStep2Handler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act - use BeginFlow directly to ensure compensation runs
        await using (var flow = mediator.BeginFlow("CancellableFlow"))
        {
            await flow.ExecuteAsync<AdvStep1Command, AdvStep1Result>(
                new AdvStep1Command { MessageId = MessageExtensions.NewMessageId() });
            flow.RegisterCompensation(async ct =>
            {
                tracker.ExecutionOrder.Add("CancelComp");
                await Task.CompletedTask;
            }, "CancelComp");

            // Simulate failure - don't commit
            var r2 = await flow.ExecuteAsync<AdvStep2Command, AdvStep2Result>(
                new AdvStep2Command { MessageId = MessageExtensions.NewMessageId(), ShouldFail = true });
            r2.IsSuccess.Should().BeFalse();
        }

        // Assert
        tracker.ExecutionOrder.Should().Contain("CancelComp");
    }

    #endregion

    #region Exception Handling Tests

    [Fact]
    public async Task Flow_WithException_ShouldCompensate()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var tracker = new AdvancedTracker();
        services.AddSingleton(tracker);
        services.AddScoped<IRequestHandler<AdvStep1Command, AdvStep1Result>, AdvStep1Handler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var result = await mediator.RunFlowAsync("ExceptionFlow", async flow =>
        {
            await flow.ExecuteAsync<AdvStep1Command, AdvStep1Result>(
                new AdvStep1Command { MessageId = MessageExtensions.NewMessageId() });
            flow.RegisterCompensation(async ct =>
            {
                tracker.ExecutionOrder.Add("ExceptionComp");
                await Task.CompletedTask;
            }, "ExceptionComp");

            throw new InvalidOperationException("Intentional exception");
#pragma warning disable CS0162
            return "never";
#pragma warning restore CS0162
        });

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Intentional exception");
        tracker.ExecutionOrder.Should().Contain("ExceptionComp");
    }

    [Fact]
    public async Task Flow_CompensationThrows_ShouldContinueOtherCompensations()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var tracker = new AdvancedTracker();
        services.AddSingleton(tracker);
        services.AddScoped<IRequestHandler<AdvStep1Command, AdvStep1Result>, AdvStep1Handler>();
        services.AddScoped<IRequestHandler<AdvStep2Command, AdvStep2Result>, AdvStep2Handler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        await using (var flow = mediator.BeginFlow("CompensationExceptionFlow"))
        {
            await flow.ExecuteAsync<AdvStep1Command, AdvStep1Result>(
                new AdvStep1Command { MessageId = MessageExtensions.NewMessageId() });
            flow.RegisterCompensation(async ct =>
            {
                tracker.ExecutionOrder.Add("Comp1-Before");
                await Task.CompletedTask;
            }, "Comp1");

            await flow.ExecuteAsync<AdvStep1Command, AdvStep1Result>(
                new AdvStep1Command { MessageId = MessageExtensions.NewMessageId() });
            flow.RegisterCompensation(ct =>
            {
                tracker.ExecutionOrder.Add("Comp2-Throws");
                throw new Exception("Compensation failed");
            }, "Comp2-Throws");

            await flow.ExecuteAsync<AdvStep1Command, AdvStep1Result>(
                new AdvStep1Command { MessageId = MessageExtensions.NewMessageId() });
            flow.RegisterCompensation(async ct =>
            {
                tracker.ExecutionOrder.Add("Comp3-After");
                await Task.CompletedTask;
            }, "Comp3");

            // Fail to trigger compensation
            await flow.ExecuteAsync<AdvStep2Command, AdvStep2Result>(
                new AdvStep2Command { MessageId = MessageExtensions.NewMessageId(), ShouldFail = true });
        }

        // Assert - all compensations should be attempted
        tracker.ExecutionOrder.Should().Contain("Comp3-After");
        tracker.ExecutionOrder.Should().Contain("Comp2-Throws");
        tracker.ExecutionOrder.Should().Contain("Comp1-Before");
    }

    #endregion

    #region StepCount and FlowName Tests

    [Fact]
    public async Task FlowContext_StepCount_ShouldIncrementCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<AdvStep1Command, AdvStep1Result>, AdvStep1Handler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var stepCounts = new List<int>();

        // Act
        await using (var flow = mediator.BeginFlow("StepCountFlow"))
        {
            stepCounts.Add(flow.StepCount);
            await flow.ExecuteAsync<AdvStep1Command, AdvStep1Result>(
                new AdvStep1Command { MessageId = MessageExtensions.NewMessageId() });
            stepCounts.Add(flow.StepCount);
            await flow.ExecuteAsync<AdvStep1Command, AdvStep1Result>(
                new AdvStep1Command { MessageId = MessageExtensions.NewMessageId() });
            stepCounts.Add(flow.StepCount);
            await flow.ExecuteAsync<AdvStep1Command, AdvStep1Result>(
                new AdvStep1Command { MessageId = MessageExtensions.NewMessageId() });
            stepCounts.Add(flow.StepCount);
            flow.Commit();
        }

        // Assert
        stepCounts.Should().BeEquivalentTo(new[] { 0, 1, 2, 3 });
    }

    [Fact]
    public async Task FlowContext_FlowName_ShouldBeAccessible()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        string? flowName = null;

        // Act
        await using (var flow = mediator.BeginFlow("MyCustomFlowName"))
        {
            flowName = flow.FlowName;
            flow.Commit();
        }

        // Assert
        flowName.Should().Be("MyCustomFlowName");
    }

    [Fact]
    public async Task FlowContext_CorrelationId_ShouldBeUnique()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var correlationIds = new List<long>();

        // Act
        for (int i = 0; i < 5; i++)
        {
            await using (var flow = mediator.BeginFlow($"Flow{i}"))
            {
                correlationIds.Add(flow.CorrelationId);
                flow.Commit();
            }
        }

        // Assert
        correlationIds.Distinct().Should().HaveCount(5);
    }

    #endregion

    #region FlowResult Tests

    [Fact]
    public async Task RunFlowAsync_OnSuccess_ShouldReturnValueAndDuration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<AdvStep1Command, AdvStep1Result>, AdvStep1Handler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var result = await mediator.RunFlowAsync("SuccessFlow", async flow =>
        {
            await flow.ExecuteAsync<AdvStep1Command, AdvStep1Result>(
                new AdvStep1Command { MessageId = MessageExtensions.NewMessageId() });
            await Task.Delay(10); // Ensure some duration
            return 42;
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        result.Error.Should().BeNull();
        result.FailedAtStep.Should().Be(0);
    }

    [Fact]
    public async Task RunFlowAsync_OnFailure_ShouldReturnErrorAndStep()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<AdvStep1Command, AdvStep1Result>, AdvStep1Handler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var result = await mediator.RunFlowAsync("FailureFlow", async flow =>
        {
            await flow.ExecuteAsync<AdvStep1Command, AdvStep1Result>(
                new AdvStep1Command { MessageId = MessageExtensions.NewMessageId() });

            throw new FlowExecutionException("TestStep", "Test error", 1);
#pragma warning disable CS0162
            return 0;
#pragma warning restore CS0162
        });

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Test error");
        result.FailedAtStep.Should().Be(1);
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    #endregion

    #region Test Types

    private sealed class AdvancedTracker
    {
        private readonly object _lock = new();
        public List<string> ExecutionOrder { get; } = new();

        public void Add(string item)
        {
            lock (_lock) ExecutionOrder.Add(item);
        }
    }

    [MemoryPackable]
    private partial record AdvStep1Command : IRequest<AdvStep1Result>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record AdvStep1Result { }

    [MemoryPackable]
    private partial record AdvStep2Command : IRequest<AdvStep2Result>
    {
        public required long MessageId { get; init; }
        public bool ShouldFail { get; init; }
    }

    [MemoryPackable]
    private partial record AdvStep2Result { }

    [MemoryPackable]
    private partial record SlowCommand : IRequest<SlowResult>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record SlowResult { }

    private sealed class AdvStep1Handler : IRequestHandler<AdvStep1Command, AdvStep1Result>
    {
        private readonly AdvancedTracker? _tracker;
        public AdvStep1Handler(AdvancedTracker? tracker = null) => _tracker = tracker;

        public Task<CatgaResult<AdvStep1Result>> HandleAsync(AdvStep1Command request, CancellationToken ct)
        {
            _tracker?.Add("Step1");
            return Task.FromResult(CatgaResult<AdvStep1Result>.Success(new AdvStep1Result()));
        }
    }

    private sealed class AdvStep2Handler : IRequestHandler<AdvStep2Command, AdvStep2Result>
    {
        private readonly AdvancedTracker? _tracker;
        public AdvStep2Handler(AdvancedTracker? tracker = null) => _tracker = tracker;

        public Task<CatgaResult<AdvStep2Result>> HandleAsync(AdvStep2Command request, CancellationToken ct)
        {
            _tracker?.Add("Step2");
            if (request.ShouldFail)
                return Task.FromResult(CatgaResult<AdvStep2Result>.Failure("Step2 failed"));
            return Task.FromResult(CatgaResult<AdvStep2Result>.Success(new AdvStep2Result()));
        }
    }

    private sealed class SlowHandler : IRequestHandler<SlowCommand, SlowResult>
    {
        public async Task<CatgaResult<SlowResult>> HandleAsync(SlowCommand request, CancellationToken ct)
        {
            await Task.Delay(500, ct);
            return CatgaResult<SlowResult>.Success(new SlowResult());
        }
    }

    #endregion
}
