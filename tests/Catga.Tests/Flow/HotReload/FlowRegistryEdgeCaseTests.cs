using Catga.Flow.HotReload;
using FluentAssertions;

namespace Catga.Tests.Flow.HotReload;

/// <summary>
/// Edge case tests for Flow Registry
/// </summary>
public class FlowRegistryEdgeCaseTests
{
    #region Special Character Flow Names

    [Fact]
    public void FlowRegistry_Register_WithSpecialCharacters_ShouldWork()
    {
        var registry = new FlowRegistry();

        registry.Register("Order-Processing_Flow.v1", new object());

        registry.Contains("Order-Processing_Flow.v1").Should().BeTrue();
    }

    [Fact]
    public void FlowRegistry_Register_WithUnicodeCharacters_ShouldWork()
    {
        var registry = new FlowRegistry();

        registry.Register("订单流程", new object());

        registry.Contains("订单流程").Should().BeTrue();
    }

    [Fact]
    public void FlowRegistry_Register_WithNumbers_ShouldWork()
    {
        var registry = new FlowRegistry();

        registry.Register("Flow123", new object());

        registry.Contains("Flow123").Should().BeTrue();
    }

    [Fact]
    public void FlowRegistry_Register_WithWhitespace_ShouldWork()
    {
        var registry = new FlowRegistry();

        registry.Register("Order Processing Flow", new object());

        registry.Contains("Order Processing Flow").Should().BeTrue();
    }

    #endregion

    #region Large Scale Tests

    [Fact]
    public void FlowRegistry_Register_ManyFlows_ShouldHandleAll()
    {
        var registry = new FlowRegistry();

        for (int i = 0; i < 1000; i++)
        {
            registry.Register($"Flow{i}", new object());
        }

        registry.GetAll().Count().Should().Be(1000);
    }

    [Fact]
    public void FlowRegistry_GetAll_WithManyFlows_ShouldReturnAll()
    {
        var registry = new FlowRegistry();

        for (int i = 0; i < 100; i++)
        {
            registry.Register($"Flow{i}", new object());
        }

        var all = registry.GetAll().ToList();
        all.Should().HaveCount(100);
        all.Should().Contain("Flow0");
        all.Should().Contain("Flow99");
    }

    [Fact]
    public void FlowRegistry_Unregister_ManyFlows_ShouldHandleAll()
    {
        var registry = new FlowRegistry();

        for (int i = 0; i < 100; i++)
        {
            registry.Register($"Flow{i}", new object());
        }

        for (int i = 0; i < 100; i++)
        {
            registry.Unregister($"Flow{i}");
        }

        registry.GetAll().Should().BeEmpty();
    }

    #endregion

    #region Config Types Tests

    [Fact]
    public void FlowRegistry_Register_WithDifferentTypes_ShouldWork()
    {
        var registry = new FlowRegistry();

        registry.Register("Flow1", "string config");
        registry.Register("Flow2", 123);
        registry.Register("Flow3", new { Name = "test" });
        registry.Register("Flow4", new List<string> { "a", "b" });

        registry.Get("Flow1").Should().Be("string config");
        registry.Get("Flow2").Should().Be(123);
        registry.Get("Flow3").Should().NotBeNull();
        registry.Get("Flow4").Should().BeOfType<List<string>>();
    }

    #endregion

    #region Ordering Tests

    [Fact]
    public void FlowRegistry_GetAll_ShouldNotGuaranteeOrder()
    {
        var registry = new FlowRegistry();

        registry.Register("C", new object());
        registry.Register("A", new object());
        registry.Register("B", new object());

        var all = registry.GetAll().ToList();
        all.Should().HaveCount(3);
        all.Should().Contain("A");
        all.Should().Contain("B");
        all.Should().Contain("C");
    }

    #endregion

    #region Get After Unregister Tests

    [Fact]
    public void FlowRegistry_Get_AfterUnregister_ShouldReturnNull()
    {
        var registry = new FlowRegistry();
        registry.Register("TestFlow", new object());
        registry.Unregister("TestFlow");

        var result = registry.Get("TestFlow");

        result.Should().BeNull();
    }

    [Fact]
    public void FlowRegistry_Contains_AfterUnregister_ShouldReturnFalse()
    {
        var registry = new FlowRegistry();
        registry.Register("TestFlow", new object());
        registry.Unregister("TestFlow");

        registry.Contains("TestFlow").Should().BeFalse();
    }

    [Fact]
    public void FlowRegistry_ReRegister_AfterUnregister_ShouldWork()
    {
        var registry = new FlowRegistry();
        var config1 = new object();
        var config2 = new object();

        registry.Register("TestFlow", config1);
        registry.Unregister("TestFlow");
        registry.Register("TestFlow", config2);

        registry.Get("TestFlow").Should().BeSameAs(config2);
    }

    #endregion
}
