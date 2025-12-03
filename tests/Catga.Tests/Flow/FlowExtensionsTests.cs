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
/// Tests for FlowExtensions (BeginFlow, RunFlowAsync)
/// </summary>
public sealed partial class FlowExtensionsTests
{
    [Fact]
    public async Task BeginFlow_ShouldCreateFlowContext()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        await using var flow = mediator.BeginFlow("TestFlow");

        // Assert
        flow.Should().NotBeNull();
        flow.FlowName.Should().Be("TestFlow");
        flow.StepCount.Should().Be(0);
        flow.CorrelationId.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task BeginFlow_WithCorrelationId_ShouldUseProvidedId()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var customCorrelationId = 12345L;

        // Act
        await using var flow = mediator.BeginFlow("TestFlow", customCorrelationId);

        // Assert
        flow.CorrelationId.Should().Be(customCorrelationId);
    }

    [Fact]
    public async Task RunFlowAsync_WithValue_ShouldReturnFlowResult()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<ExtTestCommand, ExtTestResult>, ExtTestHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var result = await mediator.RunFlowAsync("ValueFlow", async flow =>
        {
            var r = await flow.ExecuteAsync<ExtTestCommand, ExtTestResult>(
                new ExtTestCommand { MessageId = MessageExtensions.NewMessageId(), Value = 10 });
            return r.Value!.ComputedValue;
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(20); // 10 * 2
    }

    [Fact]
    public async Task RunFlowAsync_WithCancellation_ShouldPassToken()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<ExtTestCommand, ExtTestResult>, ExtTestHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await mediator.RunFlowAsync("CancelledFlow", async flow =>
        {
            cts.Token.ThrowIfCancellationRequested();
            return 0;
        }, cts.Token);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("cancel");
    }

    [Fact]
    public async Task RunFlowAsync_MultipleSequential_ShouldBeIndependent()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<ExtTestCommand, ExtTestResult>, ExtTestHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var results = new List<FlowResult<int>>();
        for (int i = 1; i <= 3; i++)
        {
            var value = i;
            var result = await mediator.RunFlowAsync($"Flow{i}", async flow =>
            {
                var r = await flow.ExecuteAsync<ExtTestCommand, ExtTestResult>(
                    new ExtTestCommand { MessageId = MessageExtensions.NewMessageId(), Value = value });
                return r.Value!.ComputedValue;
            });
            results.Add(result);
        }

        // Assert
        results.Should().HaveCount(3);
        results.All(r => r.IsSuccess).Should().BeTrue();
        results.Select(r => r.Value).Should().BeEquivalentTo(new[] { 2, 4, 6 });
    }

    [Fact]
    public async Task RunFlowAsync_Concurrent_ShouldBeIsolated()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var tracker = new ExtTracker();
        services.AddSingleton(tracker);
        services.AddScoped<IRequestHandler<ExtTestCommand, ExtTestResult>, ExtTestHandler>();
        services.AddScoped<IRequestHandler<ExtUndoCommand>, ExtUndoHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act - run 3 flows concurrently, middle one fails
        var tasks = new[]
        {
            RunFlowWithCompensation(mediator, tracker, "A", shouldFail: false),
            RunFlowWithCompensation(mediator, tracker, "B", shouldFail: true),
            RunFlowWithCompensation(mediator, tracker, "C", shouldFail: false)
        };

        var results = await Task.WhenAll(tasks);

        // Assert
        results[0].IsSuccess.Should().BeTrue();
        results[1].IsSuccess.Should().BeFalse();
        results[2].IsSuccess.Should().BeTrue();

        // Only B should have compensation
        tracker.Compensations.Should().Contain("Undo:B");
        tracker.Compensations.Should().NotContain("Undo:A");
        tracker.Compensations.Should().NotContain("Undo:C");
    }

    private static async Task<FlowResult<string>> RunFlowWithCompensation(
        ICatgaMediator mediator,
        ExtTracker tracker,
        string id,
        bool shouldFail)
    {
        return await mediator.RunFlowAsync($"Flow-{id}", async flow =>
        {
            await flow.ExecuteAsync<ExtTestCommand, ExtTestResult>(
                new ExtTestCommand { MessageId = MessageExtensions.NewMessageId(), Value = 1 });

            flow.RegisterCompensation(new ExtUndoCommand
            {
                MessageId = MessageExtensions.NewMessageId(),
                Id = id
            });

            if (shouldFail)
                throw new FlowExecutionException("TestStep", "Intentional failure", flow.StepCount);

            return id;
        });
    }

    #region Test Types

    private sealed class ExtTracker
    {
        private readonly object _lock = new();
        public List<string> Compensations { get; } = new();

        public void AddCompensation(string id)
        {
            lock (_lock) Compensations.Add(id);
        }
    }

    [MemoryPackable]
    private partial record ExtTestCommand : IRequest<ExtTestResult>
    {
        public required long MessageId { get; init; }
        public int Value { get; init; }
    }

    [MemoryPackable]
    private partial record ExtTestResult
    {
        public int ComputedValue { get; init; }
    }

    [MemoryPackable]
    private partial record ExtUndoCommand : IRequest
    {
        public required long MessageId { get; init; }
        public string Id { get; init; } = "";
    }

    private sealed class ExtTestHandler : IRequestHandler<ExtTestCommand, ExtTestResult>
    {
        public Task<CatgaResult<ExtTestResult>> HandleAsync(ExtTestCommand request, CancellationToken ct)
        {
            return Task.FromResult(CatgaResult<ExtTestResult>.Success(
                new ExtTestResult { ComputedValue = request.Value * 2 }));
        }
    }

    private sealed class ExtUndoHandler : IRequestHandler<ExtUndoCommand>
    {
        private readonly ExtTracker? _tracker;
        public ExtUndoHandler(ExtTracker? tracker = null) => _tracker = tracker;

        public Task<CatgaResult> HandleAsync(ExtUndoCommand request, CancellationToken ct)
        {
            _tracker?.AddCompensation($"Undo:{request.Id}");
            return Task.FromResult(CatgaResult.Success());
        }
    }

    #endregion
}
