using Catga.Abstractions;
using Catga.Configuration;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.DistributedId;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Integration.E2E;

/// <summary>
/// End-to-end tests for service registration and DI scenarios
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class ServiceRegistrationE2ETests
{
    [Fact]
    public void AddCatga_ShouldRegisterAllCoreServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddCatga();
        var sp = services.BuildServiceProvider();

        // Assert
        sp.GetService<ICatgaMediator>().Should().NotBeNull();
        sp.GetService<CatgaOptions>().Should().NotBeNull();
        sp.GetService<IDistributedIdGenerator>().Should().NotBeNull();
        sp.GetService<IEventTypeRegistry>().Should().NotBeNull();
    }

    [Fact]
    public void AddCatga_WithOptions_ShouldApplyConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddCatga(options =>
        {
            options.EnableLogging = false;
            options.EnableTracing = false;
            options.MaxRetryAttempts = 5;
        });
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<CatgaOptions>();

        // Assert
        options.EnableLogging.Should().BeFalse();
        options.EnableTracing.Should().BeFalse();
        options.MaxRetryAttempts.Should().Be(5);
    }

    [Fact]
    public async Task Mediator_WithRegisteredHandler_ShouldResolveAndExecute()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new TestCommand { MessageId = MessageExtensions.NewMessageId(), Value = 42 };
        var result = await mediator.SendAsync<TestCommand, TestResponse>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.DoubledValue.Should().Be(84);
    }

    [Fact]
    public async Task Mediator_WithMultipleEventHandlers_ShouldInvokeAll()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IEventHandler<TestEvent>, TestEventHandler1>();
        services.AddScoped<IEventHandler<TestEvent>, TestEventHandler2>();
        services.AddScoped<IEventHandler<TestEvent>, TestEventHandler3>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        TestEventHandler1.ReceivedCount = 0;
        TestEventHandler2.ReceivedCount = 0;
        TestEventHandler3.ReceivedCount = 0;

        // Act
        var @event = new TestEvent { MessageId = MessageExtensions.NewMessageId() };
        await mediator.PublishAsync(@event);

        // Assert
        TestEventHandler1.ReceivedCount.Should().Be(1);
        TestEventHandler2.ReceivedCount.Should().Be(1);
        TestEventHandler3.ReceivedCount.Should().Be(1);
    }

    [Fact]
    public void CatgaServiceBuilder_ShouldExposeServicesAndOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        var builder = services.AddCatga();

        // Assert
        builder.Services.Should().BeSameAs(services);
        builder.Options.Should().NotBeNull();
    }

    [Fact]
    public void CatgaOptions_Minimal_ShouldDisableAllFeatures()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddCatga(options => options.Minimal());
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<CatgaOptions>();

        // Assert
        options.EnableLogging.Should().BeFalse();
        options.EnableTracing.Should().BeFalse();
        options.EnableIdempotency.Should().BeFalse();
        options.EnableRetry.Should().BeFalse();
        options.EnableValidation.Should().BeFalse();
        options.EnableDeadLetterQueue.Should().BeFalse();
    }

    [Fact]
    public void CatgaOptions_ForDevelopment_ShouldEnableLoggingAndTracing()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddCatga(options => options.ForDevelopment());
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<CatgaOptions>();

        // Assert
        options.EnableLogging.Should().BeTrue();
        options.EnableTracing.Should().BeTrue();
        options.EnableIdempotency.Should().BeFalse();
    }

    [Fact]
    public void CatgaOptions_WithHighPerformance_ShouldOptimize()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddCatga(options => options.WithHighPerformance());
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<CatgaOptions>();

        // Assert
        options.IdempotencyShardCount.Should().Be(64);
        options.EnableRetry.Should().BeFalse();
        options.EnableValidation.Should().BeFalse();
    }

    [Fact]
    public Task ScopedMediator_DifferentScopes_ShouldGetDifferentInstances()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestHandler>();

        var sp = services.BuildServiceProvider();

        // Act
        ICatgaMediator mediator1, mediator2;
        using (var scope1 = sp.CreateScope())
        {
            mediator1 = scope1.ServiceProvider.GetRequiredService<ICatgaMediator>();
        }
        using (var scope2 = sp.CreateScope())
        {
            mediator2 = scope2.ServiceProvider.GetRequiredService<ICatgaMediator>();
        }

        // Assert
        mediator1.Should().NotBeSameAs(mediator2);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Mediator_WithScopedDependency_ShouldResolveScopedHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<ScopedDependency>();
        services.AddScoped<IRequestHandler<ScopedCommand, ScopedResponse>, ScopedHandler>();

        var sp = services.BuildServiceProvider();

        // Act
        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<ICatgaMediator>();
        var command = new ScopedCommand { MessageId = MessageExtensions.NewMessageId() };
        var result = await mediator.SendAsync<ScopedCommand, ScopedResponse>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.DependencyId.Should().NotBeEmpty();
    }

    #region Test Types

    [MemoryPackable]
    private partial record TestCommand : IRequest<TestResponse>
    {
        public required long MessageId { get; init; }
        public int Value { get; init; }
    }

    [MemoryPackable]
    private partial record TestResponse
    {
        public int DoubledValue { get; init; }
    }

    private sealed class TestHandler : IRequestHandler<TestCommand, TestResponse>
    {
        public ValueTask<CatgaResult<TestResponse>> HandleAsync(TestCommand request, CancellationToken ct = default)
        {
            return new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(new TestResponse { DoubledValue = request.Value * 2 }));
        }
    }

    [MemoryPackable]
    private partial record TestEvent : IEvent
    {
        public required long MessageId { get; init; }
    }

    private sealed class TestEventHandler1 : IEventHandler<TestEvent>
    {
        public static int ReceivedCount;
        public ValueTask HandleAsync(TestEvent @event, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ReceivedCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestEventHandler2 : IEventHandler<TestEvent>
    {
        public static int ReceivedCount;
        public ValueTask HandleAsync(TestEvent @event, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ReceivedCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestEventHandler3 : IEventHandler<TestEvent>
    {
        public static int ReceivedCount;
        public ValueTask HandleAsync(TestEvent @event, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ReceivedCount);
            return ValueTask.CompletedTask;
        }
    }

    [MemoryPackable]
    private partial record ScopedCommand : IRequest<ScopedResponse>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record ScopedResponse
    {
        public required string DependencyId { get; init; }
    }

    private sealed class ScopedDependency
    {
        public string Id { get; } = Guid.NewGuid().ToString();
    }

    private sealed class ScopedHandler : IRequestHandler<ScopedCommand, ScopedResponse>
    {
        private readonly ScopedDependency _dependency;

        public ScopedHandler(ScopedDependency dependency)
        {
            _dependency = dependency;
        }

        public ValueTask<CatgaResult<ScopedResponse>> HandleAsync(ScopedCommand request, CancellationToken ct = default)
        {
            return new ValueTask<CatgaResult<ScopedResponse>>(CatgaResult<ScopedResponse>.Success(new ScopedResponse { DependencyId = _dependency.Id }));
        }
    }

    #endregion
}
