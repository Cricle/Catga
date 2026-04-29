using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// E2E tests for Mediator and Pipeline behaviors.
/// Tests request/response, events, and pipeline behaviors.
/// </summary>
public class MediatorPipelineE2ETests
{
    [Fact]
    public async Task Mediator_SendRequest_ReturnsResponse()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseMemoryPack()
            .UseInMemory();
        services.AddSingleton<IRequestHandler<TestQuery, TestResult>, TestQueryHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var result = await mediator.SendAsync<TestQuery, TestResult>(new TestQuery("Q001"));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.QueryId.Should().Be("Q001");
        result.Value.Data.Should().Be("Result for Q001");
    }

    [Fact]
    public async Task Mediator_SendCommand_ExecutesHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseMemoryPack()
            .UseInMemory();
        services.AddSingleton<IRequestHandler<TestCommand, string>, TestCommandHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var result = await mediator.SendAsync<TestCommand, string>(new TestCommand("CMD001", "Test Data"));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Processed: CMD001");
    }

    [Fact]
    public async Task Mediator_PublishEvent_NotifiesAllHandlers()
    {
        // Arrange
        var handler1Called = false;
        var handler2Called = false;

        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseMemoryPack()
            .UseInMemory();
        services.AddSingleton<IEventHandler<TestEvent>>(new DelegateEventHandler<TestEvent>(_ => handler1Called = true));
        services.AddSingleton<IEventHandler<TestEvent>>(new DelegateEventHandler<TestEvent>(_ => handler2Called = true));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        await mediator.PublishAsync(new TestEvent { EventId = "E001" });

        // Assert
        handler1Called.Should().BeTrue();
        handler2Called.Should().BeTrue();
    }

    [Fact]
    public async Task Mediator_WithValidationBehavior_RejectsInvalidRequest()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseMemoryPack()
            .UseInMemory();
        services.AddSingleton<IRequestHandler<ValidatedCommand, string>, ValidatedCommandHandler>();
        services.AddSingleton<IValidator<ValidatedCommand>, ValidatedCommandValidator>();
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act - Invalid command (empty name)
        var invalidResult = await mediator.SendAsync<ValidatedCommand, string>(new ValidatedCommand("", 0));

        // Assert
        invalidResult.IsSuccess.Should().BeFalse();
        invalidResult.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task Mediator_WithValidationBehavior_AcceptsValidRequest()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseMemoryPack()
            .UseInMemory();
        services.AddSingleton<IRequestHandler<ValidatedCommand, string>, ValidatedCommandHandler>();
        services.AddSingleton<IValidator<ValidatedCommand>, ValidatedCommandValidator>();
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act - Valid command
        var validResult = await mediator.SendAsync<ValidatedCommand, string>(new ValidatedCommand("Test Name", 100));

        // Assert
        validResult.IsSuccess.Should().BeTrue();
        validResult.Value.Should().Be("Valid: Test Name - 100");
    }

    [Fact]
    public async Task Mediator_BatchSend_ProcessesMultipleRequests()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseMemoryPack()
            .UseInMemory();
        services.AddSingleton<IRequestHandler<TestQuery, TestResult>, TestQueryHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var queries = Enumerable.Range(1, 5)
            .Select(i => new TestQuery($"Q{i:000}"))
            .ToList();

        // Act
        var results = await mediator.SendBatchAsync<TestQuery, TestResult>(queries);

        // Assert
        results.Should().HaveCount(5);
        results.All(r => r.IsSuccess).Should().BeTrue();
    }

    [Fact]
    public async Task Mediator_BatchPublish_NotifiesAllHandlers()
    {
        // Arrange
        var receivedEvents = new List<string>();

        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseMemoryPack()
            .UseInMemory();
        services.AddSingleton<IEventHandler<TestEvent>>(new DelegateEventHandler<TestEvent>(e =>
        {
            lock (receivedEvents) receivedEvents.Add(e.EventId);
        }));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var events = Enumerable.Range(1, 10)
            .Select(i => new TestEvent { EventId = $"E{i:000}" })
            .ToList();

        // Act
        await mediator.PublishBatchAsync(events);

        // Assert
        receivedEvents.Should().HaveCount(10);
    }

    [Fact]
    public async Task Mediator_HandlerThrows_ReturnsFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseMemoryPack()
            .UseInMemory();
        services.AddSingleton<IRequestHandler<FailingCommand, string>, FailingCommandHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var result = await mediator.SendAsync<FailingCommand, string>(new FailingCommand());

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task Mediator_NoHandler_ReturnsFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseMemoryPack()
            .UseInMemory();
        // Note: No handler registered for UnhandledQuery

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var result = await mediator.SendAsync<UnhandledQuery, string>(new UnhandledQuery());

        // Assert
        result.IsSuccess.Should().BeFalse();
    }

    #region Test Types

    public record TestQuery(string QueryId) : IRequest<TestResult>
    {
        public long MessageId { get; init; }
    }
    public record TestResult(string QueryId, string Data);

    public record TestCommand(string CommandId, string Data) : IRequest<string>
    {
        public long MessageId { get; init; }
    }

    public record TestEvent : EventBase
    {
        public string EventId { get; init; } = "";
    }

    public record ValidatedCommand(string Name, int Value) : IRequest<string>
    {
        public long MessageId { get; init; }
    }

    public record FailingCommand : IRequest<string>
    {
        public long MessageId { get; init; }
    }

    public record UnhandledQuery : IRequest<string>
    {
        public long MessageId { get; init; }
    }

    public class TestQueryHandler : IRequestHandler<TestQuery, TestResult>
    {
        public ValueTask<CatgaResult<TestResult>> HandleAsync(TestQuery request, CancellationToken ct = default)
        {
            return ValueTask.FromResult(CatgaResult<TestResult>.Success(new TestResult(request.QueryId, $"Result for {request.QueryId}")));
        }
    }

    public class TestCommandHandler : IRequestHandler<TestCommand, string>
    {
        public ValueTask<CatgaResult<string>> HandleAsync(TestCommand request, CancellationToken ct = default)
        {
            return ValueTask.FromResult(CatgaResult<string>.Success($"Processed: {request.CommandId}"));
        }
    }

    public class ValidatedCommandHandler : IRequestHandler<ValidatedCommand, string>
    {
        public ValueTask<CatgaResult<string>> HandleAsync(ValidatedCommand request, CancellationToken ct = default)
        {
            return ValueTask.FromResult(CatgaResult<string>.Success($"Valid: {request.Name} - {request.Value}"));
        }
    }

    public class ValidatedCommandValidator : IValidator<ValidatedCommand>
    {
        public ValueTask<List<string>> ValidateAsync(ValidatedCommand request, CancellationToken ct = default)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(request.Name))
                errors.Add("Name is required");

            if (request.Value <= 0)
                errors.Add("Value must be positive");

            return ValueTask.FromResult(errors);
        }
    }

    public class FailingCommandHandler : IRequestHandler<FailingCommand, string>
    {
        public ValueTask<CatgaResult<string>> HandleAsync(FailingCommand request, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Intentional failure");
        }
    }

    public class DelegateEventHandler<T> : IEventHandler<T> where T : IEvent
    {
        private readonly Action<T> _handler;
        public DelegateEventHandler(Action<T> handler) => _handler = handler;
        public ValueTask HandleAsync(T @event, CancellationToken ct = default)
        {
            _handler(@event);
            return ValueTask.CompletedTask;
        }
    }

    #endregion
}
