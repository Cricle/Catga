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
/// Tests for ICompensatable interface and automatic compensation registration
/// </summary>
public sealed partial class FlowCompensatableTests
{
    [Fact]
    public async Task ManualCompensation_ShouldExecuteOnFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var tracker = new CompTracker();
        services.AddSingleton(tracker);
        services.AddScoped<IRequestHandler<CompReserveCommand, CompReserveResult>, CompReserveHandler>();
        services.AddScoped<IRequestHandler<CompReleaseCommand>, CompReleaseHandler>();
        services.AddScoped<IRequestHandler<CompFailCommand, CompFailResult>, CompFailHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        await using (var flow = mediator.BeginFlow("CompensatableFlow"))
        {
            var r1 = await flow.ExecuteAsync<CompReserveCommand, CompReserveResult>(
                new CompReserveCommand { MessageId = MessageExtensions.NewMessageId(), ItemId = "ITEM-001" });
            r1.IsSuccess.Should().BeTrue();

            // Manually register compensation
            flow.RegisterCompensation(new CompReleaseCommand
            {
                MessageId = MessageExtensions.NewMessageId(),
                ReservationId = r1.Value!.ReservationId
            });

            // Fail to trigger compensation
            var r2 = await flow.ExecuteAsync<CompFailCommand, CompFailResult>(
                new CompFailCommand { MessageId = MessageExtensions.NewMessageId() });
            r2.IsSuccess.Should().BeFalse();
        }

        // Assert
        tracker.ExecutionOrder.Should().Contain("Reserve:ITEM-001");
        tracker.ExecutionOrder.Should().Contain("Release:RES-ITEM-001");
    }

    [Fact]
    public async Task ManualCompensation_MultipleSteps_ShouldCompensateInReverseOrder()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var tracker = new CompTracker();
        services.AddSingleton(tracker);
        services.AddScoped<IRequestHandler<CompReserveCommand, CompReserveResult>, CompReserveHandler>();
        services.AddScoped<IRequestHandler<CompReleaseCommand>, CompReleaseHandler>();
        services.AddScoped<IRequestHandler<CompFailCommand, CompFailResult>, CompFailHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        await using (var flow = mediator.BeginFlow("MultiCompensatableFlow"))
        {
            var r1 = await flow.ExecuteAsync<CompReserveCommand, CompReserveResult>(
                new CompReserveCommand { MessageId = MessageExtensions.NewMessageId(), ItemId = "A" });
            flow.RegisterCompensation(new CompReleaseCommand
            {
                MessageId = MessageExtensions.NewMessageId(),
                ReservationId = r1.Value!.ReservationId
            });

            var r2 = await flow.ExecuteAsync<CompReserveCommand, CompReserveResult>(
                new CompReserveCommand { MessageId = MessageExtensions.NewMessageId(), ItemId = "B" });
            flow.RegisterCompensation(new CompReleaseCommand
            {
                MessageId = MessageExtensions.NewMessageId(),
                ReservationId = r2.Value!.ReservationId
            });

            var r3 = await flow.ExecuteAsync<CompReserveCommand, CompReserveResult>(
                new CompReserveCommand { MessageId = MessageExtensions.NewMessageId(), ItemId = "C" });
            flow.RegisterCompensation(new CompReleaseCommand
            {
                MessageId = MessageExtensions.NewMessageId(),
                ReservationId = r3.Value!.ReservationId
            });

            // Fail
            await flow.ExecuteAsync<CompFailCommand, CompFailResult>(
                new CompFailCommand { MessageId = MessageExtensions.NewMessageId() });
        }

        // Assert - reverse order
        var releases = tracker.ExecutionOrder.Where(x => x.StartsWith("Release")).ToList();
        releases.Should().ContainInOrder("Release:RES-C", "Release:RES-B", "Release:RES-A");
    }

    [Fact]
    public async Task ICompensatable_OnCommit_ShouldNotCompensate()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var tracker = new CompTracker();
        services.AddSingleton(tracker);
        services.AddScoped<IRequestHandler<CompReserveCommand, CompReserveResult>, CompReserveHandler>();
        services.AddScoped<IRequestHandler<CompReleaseCommand>, CompReleaseHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        await using (var flow = mediator.BeginFlow("CommitFlow"))
        {
            await flow.ExecuteAsync<CompReserveCommand, CompReserveResult>(
                new CompReserveCommand { MessageId = MessageExtensions.NewMessageId(), ItemId = "X" });
            flow.Commit();
        }

        // Assert
        tracker.ExecutionOrder.Should().Contain("Reserve:X");
        tracker.ExecutionOrder.Should().NotContain(x => x.StartsWith("Release"));
    }

    #region Test Types

    private sealed class CompTracker
    {
        private readonly object _lock = new();
        public List<string> ExecutionOrder { get; } = new();

        public void Add(string item)
        {
            lock (_lock) ExecutionOrder.Add(item);
        }
    }

    [MemoryPackable]
    private partial record CompReserveCommand : IRequest<CompReserveResult>
    {
        public required long MessageId { get; init; }
        public string ItemId { get; init; } = "";
    }

    [MemoryPackable]
    private partial record CompReserveResult
    {
        public string ReservationId { get; init; } = "";
    }

    [MemoryPackable]
    private partial record CompReleaseCommand : IRequest
    {
        public required long MessageId { get; init; }
        public string ReservationId { get; init; } = "";
    }

    [MemoryPackable]
    private partial record CompFailCommand : IRequest<CompFailResult>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record CompFailResult { }

    private sealed class CompReserveHandler : IRequestHandler<CompReserveCommand, CompReserveResult>
    {
        private readonly CompTracker _tracker;
        public CompReserveHandler(CompTracker tracker) => _tracker = tracker;

        public Task<CatgaResult<CompReserveResult>> HandleAsync(CompReserveCommand request, CancellationToken ct)
        {
            _tracker.Add($"Reserve:{request.ItemId}");
            return Task.FromResult(CatgaResult<CompReserveResult>.Success(
                new CompReserveResult { ReservationId = $"RES-{request.ItemId}" }));
        }
    }

    private sealed class CompReleaseHandler : IRequestHandler<CompReleaseCommand>
    {
        private readonly CompTracker _tracker;
        public CompReleaseHandler(CompTracker tracker) => _tracker = tracker;

        public Task<CatgaResult> HandleAsync(CompReleaseCommand request, CancellationToken ct)
        {
            _tracker.Add($"Release:{request.ReservationId}");
            return Task.FromResult(CatgaResult.Success());
        }
    }

    private sealed class CompFailHandler : IRequestHandler<CompFailCommand, CompFailResult>
    {
        public Task<CatgaResult<CompFailResult>> HandleAsync(CompFailCommand request, CancellationToken ct)
        {
            return Task.FromResult(CatgaResult<CompFailResult>.Failure("Intentional failure"));
        }
    }

    #endregion
}
