using Catga.Flow.Dsl;
using Catga.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Catga.DependencyInjection;

namespace Catga.Tests.SourceGeneration;

/// <summary>
/// Tests for Flow DSL source generation.
/// Verifies that flow configurations are discovered and registered without reflection.
/// </summary>
public class FlowDslGenerationTests
{
    [Fact]
    public void SourceGenerator_DiscoversAllFlowConfigs()
    {
        // Arrange - Create some test flows that should be discovered
        var services = new ServiceCollection();

        // Act - Call the generated registration method
        services.AddGeneratedFlows();

        // Assert - Verify flows are registered
        var provider = services.BuildServiceProvider();

        // The generated method should have registered these
        var testFlow = provider.GetService<TestGenerationFlow>();
        testFlow.Should().NotBeNull("TestGenerationFlow should be registered");

        var anotherFlow = provider.GetService<AnotherTestFlow>();
        anotherFlow.Should().NotBeNull("AnotherTestFlow should be registered");
    }

    [Fact]
    public void SourceGenerator_CreatesIndividualRegistrationMethods()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act - Use generated individual registration methods
        services.AddTestGenerationFlow();
        services.AddAnotherTestFlow();

        // Assert
        var provider = services.BuildServiceProvider();

        var executor1 = provider.GetService<DslFlowExecutor<TestFlowState, TestGenerationFlow>>();
        executor1.Should().NotBeNull("Executor for TestGenerationFlow should be registered");

        var executor2 = provider.GetService<DslFlowExecutor<AnotherFlowState, AnotherTestFlow>>();
        executor2.Should().NotBeNull("Executor for AnotherTestFlow should be registered");
    }

    [Fact]
    public void SourceGenerator_ProvidesFlowMetadata()
    {
        // Act - Get generated metadata
        var flows = CatgaGeneratedFlowRegistrations.GetRegisteredFlows();

        // Assert
        flows.Should().NotBeEmpty("Should have discovered flows");
        flows.Should().Contain(f => f.Name == nameof(TestGenerationFlow));
        flows.Should().Contain(f => f.Name == nameof(AnotherTestFlow));

        var testFlow = flows.First(f => f.Name == nameof(TestGenerationFlow));
        testFlow.StateType.Should().Be(typeof(TestFlowState));
        testFlow.FlowType.Should().Be(typeof(TestGenerationFlow));
    }
}

// Test flow configurations for source generation
public class TestGenerationFlow : FlowConfig<TestFlowState>
{
    protected override void Configure(IFlowBuilder<TestFlowState> flow)
    {
        flow.Name("test-generation-flow");
        flow.Send(s => new TestCommand { Id = s.FlowId! });
    }
}

public class AnotherTestFlow : FlowConfig<AnotherFlowState>
{
    protected override void Configure(IFlowBuilder<AnotherFlowState> flow)
    {
        flow.Name("another-test-flow");
        flow.If(s => s.Value > 10)
            .Send(s => new ProcessCommand { Value = s.Value })
        .EndIf();
    }
}

// Test states
public class TestFlowState : IFlowState
{
    public string? FlowId { get; set; }
    public bool HasChanges => false;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class AnotherFlowState : IFlowState
{
    public string? FlowId { get; set; }
    public int Value { get; set; }
    public bool HasChanges => false;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

// Test commands
public record TestCommand : IRequest<string>
{
    public string Id { get; init; } = string.Empty;
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record ProcessCommand : IRequest<bool>
{
    public int Value { get; init; }
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
