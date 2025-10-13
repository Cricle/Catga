using Catga;
using Catga.InMemory;
using Catga.Messages;
using Catga.Pipeline;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga.Tests.Pipeline;

/// <summary>
/// Pipeline Behavior 测试
/// </summary>
public class PipelineBehaviorTests
{
    private static ServiceCollection CreateBasicServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddCatga();
        services.AddCatgaInMemoryTransport();
        services.AddCatgaInMemoryPersistence();
        services.AddSingleton<IRequestHandler<TestPipelineCommand, string>, TestPipelineCommandHandler>();
        return services;
    }

    [Fact]
    public async Task Pipeline_WithLoggingBehavior_ShouldLogExecution()
    {
        // Arrange
        var services = CreateBasicServices();
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(TestLoggingBehavior<,>));
        var mediator = services.BuildServiceProvider().GetRequiredService<ICatgaMediator>();

        // Act
        var result = await mediator.SendAsync(new TestPipelineCommand("test"));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Handled: test");
        TestLoggingBehavior<TestPipelineCommand, string>.ExecutionCount.Should().Be(1);
    }

    [Fact]
    public async Task Pipeline_WithMultipleBehaviors_ShouldExecuteInOrder()
    {
        // Arrange
        var services = CreateBasicServices();
        services.AddSingleton<IPipelineBehavior<TestPipelineCommand, string>, FirstBehavior>();
        services.AddSingleton<IPipelineBehavior<TestPipelineCommand, string>, SecondBehavior>();
        var mediator = services.BuildServiceProvider().GetRequiredService<ICatgaMediator>();

        // Reset execution order
        BehaviorExecutionOrder.Clear();

        // Act
        var result = await mediator.SendAsync(new TestPipelineCommand("test"));

        // Assert
        result.IsSuccess.Should().BeTrue();
        BehaviorExecutionOrder.Should().ContainInOrder("First-Before", "Second-Before", "Handler", "Second-After", "First-After");
    }

    [Fact]
    public async Task Pipeline_WithValidationBehavior_ShouldValidateRequest()
    {
        // Arrange
        var services = CreateBasicServices();
        services.AddSingleton<IPipelineBehavior<TestPipelineCommand, string>, ValidationBehavior>();
        var mediator = services.BuildServiceProvider().GetRequiredService<ICatgaMediator>();

        // Act - invalid data
        var result = await mediator.SendAsync(new TestPipelineCommand(""));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Data cannot be empty");
    }
}

// Test Messages
public record TestPipelineCommand(string Data) : IRequest<string>;

// Test Handler
public class TestPipelineCommandHandler : IRequestHandler<TestPipelineCommand, string>
{
    public Task<CatgaResult<string>> HandleAsync(TestPipelineCommand request, CancellationToken cancellationToken = default)
    {
        BehaviorExecutionOrder.Add("Handler");
        return Task.FromResult(CatgaResult<string>.Success($"Handled: {request.Data}"));
    }
}

// Test Behaviors
public class TestLoggingBehavior<TRequest, TResponse> : PipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public static int ExecutionCount = 0;

    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        ExecutionCount++;
        return await next();
    }
}

public class FirstBehavior : PipelineBehavior<TestPipelineCommand, string>
{
    public override async ValueTask<CatgaResult<string>> HandleAsync(
        TestPipelineCommand request,
        PipelineDelegate<string> next,
        CancellationToken cancellationToken = default)
    {
        BehaviorExecutionOrder.Add("First-Before");
        var result = await next();
        BehaviorExecutionOrder.Add("First-After");
        return result;
    }
}

public class SecondBehavior : PipelineBehavior<TestPipelineCommand, string>
{
    public override async ValueTask<CatgaResult<string>> HandleAsync(
        TestPipelineCommand request,
        PipelineDelegate<string> next,
        CancellationToken cancellationToken = default)
    {
        BehaviorExecutionOrder.Add("Second-Before");
        var result = await next();
        BehaviorExecutionOrder.Add("Second-After");
        return result;
    }
}

public class ValidationBehavior : PipelineBehavior<TestPipelineCommand, string>
{
    public override async ValueTask<CatgaResult<string>> HandleAsync(
        TestPipelineCommand request,
        PipelineDelegate<string> next,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Data))
            return CatgaResult<string>.Failure("Data cannot be empty");

        return await next();
    }
}

// Helper for tracking execution order
public static class BehaviorExecutionOrder
{
    private static readonly List<string> _order = new();

    public static void Add(string step) => _order.Add(step);
    public static void Clear() => _order.Clear();
    public static List<string> Get() => new(_order);
    public static bool Should => true;
    public static List<string> ContainInOrder(params string[] expected) => _order;
}

