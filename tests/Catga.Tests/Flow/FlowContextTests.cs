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
/// Tests for FlowContext and automatic compensation
/// </summary>
public sealed partial class FlowContextTests
{
    [Fact]
    public async Task FlowContext_OnSuccess_ShouldNotCompensate()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var tracker = new ExecutionTracker();
        services.AddSingleton(tracker);
        services.AddScoped<IRequestHandler<TestCreateCommand, TestCreateResult>, TestCreateHandler>();
        services.AddScoped<IRequestHandler<TestCancelCommand>, TestCancelHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        await using (var flow = mediator.BeginFlow("TestFlow"))
        {
            var result = await flow.ExecuteAsync<TestCreateCommand, TestCreateResult>(
                new TestCreateCommand { MessageId = MessageExtensions.NewMessageId() });

            result.IsSuccess.Should().BeTrue();
            flow.Commit();
        }

        // Assert
        tracker.CreateCalled.Should().BeTrue();
        tracker.CancelCalled.Should().BeFalse(); // No compensation on success
    }

    [Fact]
    public async Task FlowContext_OnFailure_ShouldCompensate()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var tracker = new ExecutionTracker();
        services.AddSingleton(tracker);
        services.AddScoped<IRequestHandler<TestCreateCommand, TestCreateResult>, TestCreateHandler>();
        services.AddScoped<IRequestHandler<TestFailCommand, TestFailResult>, TestFailHandler>();
        services.AddScoped<IRequestHandler<TestCancelCommand>, TestCancelHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        await using (var flow = mediator.BeginFlow("TestFlow"))
        {
            // Step 1: Success
            var result1 = await flow.ExecuteAsync<TestCreateCommand, TestCreateResult>(
                new TestCreateCommand { MessageId = MessageExtensions.NewMessageId() });
            result1.IsSuccess.Should().BeTrue();

            // Manually register compensation (since TestCreateCommand doesn't implement ICompensatable)
            flow.RegisterCompensation(new TestCancelCommand
            {
                MessageId = MessageExtensions.NewMessageId(),
                OrderId = result1.Value!.OrderId
            });

            // Step 2: Failure
            var result2 = await flow.ExecuteAsync<TestFailCommand, TestFailResult>(
                new TestFailCommand { MessageId = MessageExtensions.NewMessageId() });
            result2.IsSuccess.Should().BeFalse();

            // Don't commit - let dispose handle compensation
        }

        // Assert
        tracker.CreateCalled.Should().BeTrue();
        tracker.FailCalled.Should().BeTrue();
        tracker.CancelCalled.Should().BeTrue(); // Compensation executed
    }

    [Fact]
    public async Task FlowContext_MultipleSteps_ShouldCompensateInReverseOrder()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var tracker = new ExecutionTracker();
        services.AddSingleton(tracker);
        services.AddScoped<IRequestHandler<Step1Command, Step1Result>, Step1Handler>();
        services.AddScoped<IRequestHandler<Step2Command, Step2Result>, Step2Handler>();
        services.AddScoped<IRequestHandler<Step3Command, Step3Result>, Step3Handler>();
        services.AddScoped<IRequestHandler<Undo1Command>, Undo1Handler>();
        services.AddScoped<IRequestHandler<Undo2Command>, Undo2Handler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        await using (var flow = mediator.BeginFlow("MultiStepFlow"))
        {
            var r1 = await flow.ExecuteAsync<Step1Command, Step1Result>(
                new Step1Command { MessageId = MessageExtensions.NewMessageId() });
            flow.RegisterCompensation(new Undo1Command { MessageId = MessageExtensions.NewMessageId() });

            var r2 = await flow.ExecuteAsync<Step2Command, Step2Result>(
                new Step2Command { MessageId = MessageExtensions.NewMessageId() });
            flow.RegisterCompensation(new Undo2Command { MessageId = MessageExtensions.NewMessageId() });

            // Step 3 fails
            var r3 = await flow.ExecuteAsync<Step3Command, Step3Result>(
                new Step3Command { MessageId = MessageExtensions.NewMessageId(), ShouldFail = true });
            r3.IsSuccess.Should().BeFalse();
        }

        // Assert - compensation should be in reverse order
        tracker.ExecutionOrder.Should().ContainInOrder("Step1", "Step2", "Step3", "Undo2", "Undo1");
    }

    [Fact]
    public async Task RunFlowAsync_ShouldReturnFlowResult()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var tracker = new ExecutionTracker();
        services.AddSingleton(tracker);
        services.AddScoped<IRequestHandler<TestCreateCommand, TestCreateResult>, TestCreateHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var result = await mediator.RunFlowAsync("TestFlow", async flow =>
        {
            var r = await flow.ExecuteAsync<TestCreateCommand, TestCreateResult>(
                new TestCreateCommand { MessageId = MessageExtensions.NewMessageId() });
            return r.Value!.OrderId;
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeGreaterThan(0);
    }

    [Fact]
    public void FlowContext_Current_ShouldBeNullOutsideFlow()
    {
        // Assert
        FlowContext.Current.Should().BeNull();
        FlowContext.IsInFlow.Should().BeFalse();
    }

    #region Test Types

    private sealed class ExecutionTracker
    {
        public bool CreateCalled { get; set; }
        public bool CancelCalled { get; set; }
        public bool FailCalled { get; set; }
        public List<string> ExecutionOrder { get; } = new();
    }

    [MemoryPackable]
    private partial record TestCreateCommand : IRequest<TestCreateResult>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record TestCreateResult
    {
        public long OrderId { get; init; }
    }

    [MemoryPackable]
    private partial record TestCancelCommand : IRequest
    {
        public required long MessageId { get; init; }
        public long OrderId { get; init; }
    }

    [MemoryPackable]
    private partial record TestFailCommand : IRequest<TestFailResult>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record TestFailResult { }

    private sealed class TestCreateHandler : IRequestHandler<TestCreateCommand, TestCreateResult>
    {
        private readonly ExecutionTracker _tracker;
        public TestCreateHandler(ExecutionTracker tracker) => _tracker = tracker;

        public Task<CatgaResult<TestCreateResult>> HandleAsync(TestCreateCommand request, CancellationToken ct)
        {
            _tracker.CreateCalled = true;
            return Task.FromResult(CatgaResult<TestCreateResult>.Success(
                new TestCreateResult { OrderId = MessageExtensions.NewMessageId() }));
        }
    }

    private sealed class TestCancelHandler : IRequestHandler<TestCancelCommand>
    {
        private readonly ExecutionTracker _tracker;
        public TestCancelHandler(ExecutionTracker tracker) => _tracker = tracker;

        public Task<CatgaResult> HandleAsync(TestCancelCommand request, CancellationToken ct)
        {
            _tracker.CancelCalled = true;
            return Task.FromResult(CatgaResult.Success());
        }
    }

    private sealed class TestFailHandler : IRequestHandler<TestFailCommand, TestFailResult>
    {
        private readonly ExecutionTracker _tracker;
        public TestFailHandler(ExecutionTracker tracker) => _tracker = tracker;

        public Task<CatgaResult<TestFailResult>> HandleAsync(TestFailCommand request, CancellationToken ct)
        {
            _tracker.FailCalled = true;
            return Task.FromResult(CatgaResult<TestFailResult>.Failure("Intentional failure"));
        }
    }

    // Multi-step test types
    [MemoryPackable]
    private partial record Step1Command : IRequest<Step1Result> { public required long MessageId { get; init; } }
    [MemoryPackable]
    private partial record Step1Result { }
    [MemoryPackable]
    private partial record Step2Command : IRequest<Step2Result> { public required long MessageId { get; init; } }
    [MemoryPackable]
    private partial record Step2Result { }
    [MemoryPackable]
    private partial record Step3Command : IRequest<Step3Result>
    {
        public required long MessageId { get; init; }
        public bool ShouldFail { get; init; }
    }
    [MemoryPackable]
    private partial record Step3Result { }
    [MemoryPackable]
    private partial record Undo1Command : IRequest { public required long MessageId { get; init; } }
    [MemoryPackable]
    private partial record Undo2Command : IRequest { public required long MessageId { get; init; } }

    private sealed class Step1Handler : IRequestHandler<Step1Command, Step1Result>
    {
        private readonly ExecutionTracker _tracker;
        public Step1Handler(ExecutionTracker tracker) => _tracker = tracker;
        public Task<CatgaResult<Step1Result>> HandleAsync(Step1Command request, CancellationToken ct)
        {
            _tracker.ExecutionOrder.Add("Step1");
            return Task.FromResult(CatgaResult<Step1Result>.Success(new Step1Result()));
        }
    }

    private sealed class Step2Handler : IRequestHandler<Step2Command, Step2Result>
    {
        private readonly ExecutionTracker _tracker;
        public Step2Handler(ExecutionTracker tracker) => _tracker = tracker;
        public Task<CatgaResult<Step2Result>> HandleAsync(Step2Command request, CancellationToken ct)
        {
            _tracker.ExecutionOrder.Add("Step2");
            return Task.FromResult(CatgaResult<Step2Result>.Success(new Step2Result()));
        }
    }

    private sealed class Step3Handler : IRequestHandler<Step3Command, Step3Result>
    {
        private readonly ExecutionTracker _tracker;
        public Step3Handler(ExecutionTracker tracker) => _tracker = tracker;
        public Task<CatgaResult<Step3Result>> HandleAsync(Step3Command request, CancellationToken ct)
        {
            _tracker.ExecutionOrder.Add("Step3");
            if (request.ShouldFail)
                return Task.FromResult(CatgaResult<Step3Result>.Failure("Step3 failed"));
            return Task.FromResult(CatgaResult<Step3Result>.Success(new Step3Result()));
        }
    }

    private sealed class Undo1Handler : IRequestHandler<Undo1Command>
    {
        private readonly ExecutionTracker _tracker;
        public Undo1Handler(ExecutionTracker tracker) => _tracker = tracker;
        public Task<CatgaResult> HandleAsync(Undo1Command request, CancellationToken ct)
        {
            _tracker.ExecutionOrder.Add("Undo1");
            return Task.FromResult(CatgaResult.Success());
        }
    }

    private sealed class Undo2Handler : IRequestHandler<Undo2Command>
    {
        private readonly ExecutionTracker _tracker;
        public Undo2Handler(ExecutionTracker tracker) => _tracker = tracker;
        public Task<CatgaResult> HandleAsync(Undo2Command request, CancellationToken ct)
        {
            _tracker.ExecutionOrder.Add("Undo2");
            return Task.FromResult(CatgaResult.Success());
        }
    }

    #endregion
}
