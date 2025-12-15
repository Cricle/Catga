using Catga.Flow.Dsl;
using Catga.Flow.HotReload;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Flow.HotReload;

/// <summary>
/// Tests for TypedFlowRegistry
/// </summary>
public class TypedFlowRegistryTests
{
    private class TestState : IFlowState
    {
        public string Id { get; set; } = "test-id";
    }

    [Fact]
    public void TypedFlowRegistry_CanBeCreated()
    {
        var registry = new FlowRegistry();
        var typedRegistry = new TypedFlowRegistry<TestState>(registry);

        typedRegistry.Should().NotBeNull();
    }

    [Fact]
    public void TypedFlowRegistry_Register_ShouldAddToUnderlyingRegistry()
    {
        var registry = new FlowRegistry();
        var typedRegistry = new TypedFlowRegistry<TestState>(registry);
        var mockBuilder = Substitute.For<IFlowBuilder<TestState>>();

        typedRegistry.Register("TestFlow", mockBuilder);

        registry.Contains("TestFlow").Should().BeTrue();
    }

    [Fact]
    public void TypedFlowRegistry_Get_ShouldReturnTypedBuilder()
    {
        var registry = new FlowRegistry();
        var typedRegistry = new TypedFlowRegistry<TestState>(registry);
        var mockBuilder = Substitute.For<IFlowBuilder<TestState>>();

        typedRegistry.Register("TestFlow", mockBuilder);
        var result = typedRegistry.Get("TestFlow");

        result.Should().NotBeNull();
        result.Should().BeSameAs(mockBuilder);
    }

    [Fact]
    public void TypedFlowRegistry_Get_NonExistent_ShouldReturnNull()
    {
        var registry = new FlowRegistry();
        var typedRegistry = new TypedFlowRegistry<TestState>(registry);

        var result = typedRegistry.Get("NonExistent");

        result.Should().BeNull();
    }

    [Fact]
    public void TypedFlowRegistry_Get_WrongType_ShouldReturnNull()
    {
        var registry = new FlowRegistry();
        var typedRegistry = new TypedFlowRegistry<TestState>(registry);

        registry.Register("TestFlow", new object()); // Wrong type

        var result = typedRegistry.Get("TestFlow");

        result.Should().BeNull();
    }

    [Fact]
    public void TypedFlowRegistry_MultipleFlows_ShouldWork()
    {
        var registry = new FlowRegistry();
        var typedRegistry = new TypedFlowRegistry<TestState>(registry);

        var builder1 = Substitute.For<IFlowBuilder<TestState>>();
        var builder2 = Substitute.For<IFlowBuilder<TestState>>();

        typedRegistry.Register("Flow1", builder1);
        typedRegistry.Register("Flow2", builder2);

        typedRegistry.Get("Flow1").Should().BeSameAs(builder1);
        typedRegistry.Get("Flow2").Should().BeSameAs(builder2);
    }
}
