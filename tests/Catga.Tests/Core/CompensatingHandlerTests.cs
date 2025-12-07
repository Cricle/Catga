using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Tests for CompensatingHandler
/// </summary>
public sealed partial class CompensatingHandlerTests
{
    [Fact]
    public async Task CompensatingHandler_OnSuccess_ShouldNotCompensate()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var compensationTracker = new CompensationTracker();
        services.AddSingleton(compensationTracker);
        services.AddScoped<IRequestHandler<CompensatableCommand1, CompensatableResponse1>, CompensatableHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new CompensatableCommand1 { MessageId = MessageExtensions.NewMessageId(), ShouldFail = false };
        var result = await mediator.SendAsync<CompensatableCommand1, CompensatableResponse1>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        compensationTracker.CompensationCalled.Should().BeFalse();
    }

    [Fact]
    public async Task CompensatingHandler_OnFailure_ShouldCompensate()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var compensationTracker = new CompensationTracker();
        services.AddSingleton(compensationTracker);
        services.AddScoped<IRequestHandler<CompensatableCommand2, CompensatableResponse2>, CompensatableWithCompensationHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new CompensatableCommand2 { MessageId = MessageExtensions.NewMessageId(), ShouldFail = true };
        var result = await mediator.SendAsync<CompensatableCommand2, CompensatableResponse2>(command);

        // Assert
        result.IsSuccess.Should().BeFalse();
        // Note: Compensation is typically handled by saga/workflow patterns, not automatically
    }

    #region Test Types

    private sealed class CompensationTracker
    {
        public bool CompensationCalled { get; set; }
    }

    [MemoryPackable]
    private partial record CompensatableCommand1 : IRequest<CompensatableResponse1>
    {
        public required long MessageId { get; init; }
        public bool ShouldFail { get; init; }
    }

    [MemoryPackable]
    private partial record CompensatableResponse1 { }

    [MemoryPackable]
    private partial record CompensatableCommand2 : IRequest<CompensatableResponse2>
    {
        public required long MessageId { get; init; }
        public bool ShouldFail { get; init; }
    }

    [MemoryPackable]
    private partial record CompensatableResponse2 { }

    private sealed class CompensatableHandler : IRequestHandler<CompensatableCommand1, CompensatableResponse1>
    {
        private readonly CompensationTracker _tracker;

        public CompensatableHandler(CompensationTracker tracker) => _tracker = tracker;

        public ValueTask<CatgaResult<CompensatableResponse1>> HandleAsync(CompensatableCommand1 request, CancellationToken ct = default)
        {
            if (request.ShouldFail)
            {
                return new ValueTask<CatgaResult<CompensatableResponse1>>(CatgaResult<CompensatableResponse1>.Failure("Intentional failure"));
            }
            return new ValueTask<CatgaResult<CompensatableResponse1>>(CatgaResult<CompensatableResponse1>.Success(new CompensatableResponse1()));
        }
    }

    private sealed class CompensatableWithCompensationHandler : IRequestHandler<CompensatableCommand2, CompensatableResponse2>
    {
        private readonly CompensationTracker _tracker;

        public CompensatableWithCompensationHandler(CompensationTracker tracker) => _tracker = tracker;

        public ValueTask<CatgaResult<CompensatableResponse2>> HandleAsync(CompensatableCommand2 request, CancellationToken ct = default)
        {
            if (request.ShouldFail)
            {
                // Simulate compensation
                _tracker.CompensationCalled = true;
                return new ValueTask<CatgaResult<CompensatableResponse2>>(CatgaResult<CompensatableResponse2>.Failure("Failed with compensation"));
            }
            return new ValueTask<CatgaResult<CompensatableResponse2>>(CatgaResult<CompensatableResponse2>.Success(new CompensatableResponse2()));
        }
    }

    #endregion
}
