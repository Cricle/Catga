using System;
using System.Threading;
using System.Threading.Tasks;
using Catga.Flow.Dsl;
using Catga.Flow.Extensions;
using Catga.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using Catga.DependencyInjection;

namespace Catga.Tests.Flow;

/// <summary>
/// Integration tests for Flow DSL registration.
/// Tests the complete registration pipeline from source generation to runtime execution.
/// </summary>
public class FlowDslRegistrationIntegrationTests
{
    [Fact]
    public void AddFlowDsl_RegistersAllGeneratedFlows()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ICatgaMediator>());
        services.AddSingleton(Substitute.For<IMessageSerializer>());

        // Act
        services.AddFlowDsl(options =>
        {
            options.AutoRegisterFlows = true;
        });

        // Assert
        var provider = services.BuildServiceProvider();

        // Store should be registered
        var store = provider.GetService<IDslFlowStore>();
        store.Should().NotBeNull();
        store.Should().BeOfType<InMemoryDslFlowStore>();

        // Generated flows should be available
        var registrations = CatgaGeneratedFlowRegistrations.GetRegisteredFlows();
        registrations.Should().NotBeEmpty("Source generator should discover flows");
    }

    [Fact]
    public void AddFlowDslWithRedis_ConfiguresRedisStorage()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ICatgaMediator>());
        services.AddSingleton(Substitute.For<IMessageSerializer>());

        // Mock Redis connection
        var redisConnection = Substitute.For<StackExchange.Redis.IConnectionMultiplexer>();
        services.AddSingleton(redisConnection);

        // Act
        services.AddFlowDslWithRedis("localhost:6379", options =>
        {
            options.RedisPrefix = "test:";
            options.AutoRegisterFlows = false; // Don't auto-register for this test
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var store = provider.GetService<IDslFlowStore>();

        store.Should().NotBeNull();
        store.Should().BeOfType<Catga.Persistence.Redis.Flow.RedisDslFlowStore>();
    }

    [Fact]
    public void ConfigureFlowDsl_FluentBuilder_RegistersCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ICatgaMediator>());
        services.AddSingleton(Substitute.For<IMessageSerializer>());

        // Act
        services.ConfigureFlowDsl(flow => flow
            .UseInMemoryStorage()
            .RegisterGeneratedFlows()
            .RegisterFlow<IntegrationTestState, IntegrationTestFlow>()
            .WithMetrics()
            .WithRetryPolicy(3)
            .WithStepTimeout(TimeSpan.FromMinutes(5)));

        // Assert
        var provider = services.BuildServiceProvider();

        // Specific flow should be registered
        var testFlow = provider.GetService<IntegrationTestFlow>();
        testFlow.Should().NotBeNull();

        // Executor should be available
        var executor = provider.GetService<DslFlowExecutor<IntegrationTestState, IntegrationTestFlow>>();
        executor.Should().NotBeNull();
    }

    [Fact]
    public void AddFlow_ManualRegistration_WorksCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ICatgaMediator>());
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();

        // Act
        services.AddFlow<IntegrationTestState, IntegrationTestFlow>(ServiceLifetime.Scoped);

        // Assert
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();

        // Flow config should be registered as both interfaces
        var flowAsInterface = scope.ServiceProvider.GetService<FlowConfig<IntegrationTestState>>();
        flowAsInterface.Should().NotBeNull();
        flowAsInterface.Should().BeOfType<IntegrationTestFlow>();

        var flowAsConcrete = scope.ServiceProvider.GetService<IntegrationTestFlow>();
        flowAsConcrete.Should().NotBeNull();

        // Executor should be transient
        var executor1 = provider.GetService<DslFlowExecutor<IntegrationTestState, IntegrationTestFlow>>();
        var executor2 = provider.GetService<DslFlowExecutor<IntegrationTestState, IntegrationTestFlow>>();

        executor1.Should().NotBeNull();
        executor2.Should().NotBeNull();
        executor1.Should().NotBeSameAs(executor2, "Executor should be transient");
    }

    [Fact]
    public async Task FlowExecutor_CanBeCreatedAndRun()
    {
        // Arrange
        var services = new ServiceCollection();
        var mediator = Substitute.For<ICatgaMediator>();
        services.AddSingleton(mediator);
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();

        services.AddFlow<IntegrationTestState, IntegrationTestFlow>();

        var provider = services.BuildServiceProvider();

        // Setup mediator
        mediator.SendAsync<IntegrationCommand, string>(
            Arg.Any<IntegrationCommand>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("test-result")));

        // Act
        var executor = provider.CreateFlowExecutor<IntegrationTestState, IntegrationTestFlow>();

        var state = new IntegrationTestState
        {
            FlowId = "integration-test-001",
            Value = 42
        };

        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedValue.Should().Be("test-result");

        // Verify mediator was called
        await mediator.Received(1).SendAsync<IntegrationCommand, string>(
            Arg.Is<IntegrationCommand>(c => c.Value == 42),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void GetRegisteredFlows_ProvidesMetadata()
    {
        // Act
        var flows = CatgaGeneratedFlowRegistrations.GetRegisteredFlows();

        // Assert
        flows.Should().NotBeNull();

        // If there are any generated flows, verify their structure
        foreach (var flow in flows)
        {
            flow.Name.Should().NotBeNullOrWhiteSpace();
            flow.StateType.Should().NotBeNull();
            flow.FlowType.Should().NotBeNull();

            // State type should implement IFlowState
            flow.StateType.Should().Implement<IFlowState>();

            // Flow type should inherit from FlowConfig<>
            var baseType = flow.FlowType.BaseType;
            while (baseType != null)
            {
                if (baseType.IsGenericType &&
                    baseType.GetGenericTypeDefinition() == typeof(FlowConfig<>))
                {
                    break;
                }
                baseType = baseType.BaseType;
            }
            baseType.Should().NotBeNull($"{flow.FlowType.Name} should inherit from FlowConfig<>");
        }
    }

    [Fact]
    public async Task MultipleFlows_CanRunIndependently()
    {
        // Arrange
        var services = new ServiceCollection();
        var mediator = Substitute.For<ICatgaMediator>();
        services.AddSingleton(mediator);
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();

        services.AddFlow<IntegrationTestState, IntegrationTestFlow>();
        services.AddFlow<AnotherTestState, AnotherIntegrationFlow>();

        var provider = services.BuildServiceProvider();

        // Setup mediator for both flow types
        mediator.SendAsync<IntegrationCommand, string>(Arg.Any<IntegrationCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("integration-result")));

        mediator.SendAsync<AnotherCommand, int>(Arg.Any<AnotherCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<int>>(CatgaResult<int>.Success(100)));

        // Act
        var executor1 = provider.GetService<DslFlowExecutor<IntegrationTestState, IntegrationTestFlow>>();
        var executor2 = provider.GetService<DslFlowExecutor<AnotherTestState, AnotherIntegrationFlow>>();

        var state1 = new IntegrationTestState { FlowId = "flow-1", Value = 10 };
        var state2 = new AnotherTestState { FlowId = "flow-2", Data = "test" };

        var result1 = await executor1!.RunAsync(state1);
        var result2 = await executor2!.RunAsync(state2);

        // Assert
        result1.IsSuccess.Should().BeTrue();
        result1.State.ProcessedValue.Should().Be("integration-result");

        result2.IsSuccess.Should().BeTrue();
        result2.State.Result.Should().Be(100);
    }

    [Fact]
    public void AddAllGeneratedFlows_ConvenienceMethod_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ICatgaMediator>());
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();

        // Act
        services.AddAllGeneratedFlows();

        // Assert
        var provider = services.BuildServiceProvider();

        // Should be able to resolve any generated flow
        // The actual flows depend on what the source generator finds
        var registrations = CatgaGeneratedFlowRegistrations.GetRegisteredFlows();

        foreach (var registration in registrations.Take(3)) // Test first 3 to avoid long test
        {
            var flowService = provider.GetService(registration.FlowType);
            flowService.Should().NotBeNull($"{registration.Name} should be registered");
        }
    }
}

// Test flow configurations
public class IntegrationTestFlow : FlowConfig<IntegrationTestState>
{
    protected override void Configure(IFlowBuilder<IntegrationTestState> flow)
    {
        flow.Name("integration-test-flow");

        flow.Send(s => new IntegrationCommand { Value = s.Value })
            .Into((s, result) => s.ProcessedValue = result.Value);
    }
}

public class AnotherIntegrationFlow : FlowConfig<AnotherTestState>
{
    protected override void Configure(IFlowBuilder<AnotherTestState> flow)
    {
        flow.Name("another-integration-flow");

        flow.Send(s => new AnotherCommand { Data = s.Data })
            .Into((s, result) => s.Result = result.Value);
    }
}

// Test states
public class IntegrationTestState : IFlowState
{
    public string? FlowId { get; set; }
    public int Value { get; set; }
    public string ProcessedValue { get; set; } = string.Empty;

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class AnotherTestState : IFlowState
{
    public string? FlowId { get; set; }
    public string Data { get; set; } = string.Empty;
    public int Result { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

// Test commands
public record IntegrationCommand : IRequest<string>
{
    public int Value { get; init; }
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record AnotherCommand : IRequest<int>
{
    public string Data { get; init; } = string.Empty;
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
