using Catga.Flow.HotReload;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Tests.Flow.HotReload;

/// <summary>
/// Tests for FlowHotReloadExtensions
/// </summary>
public class FlowHotReloadExtensionsTests
{
    [Fact]
    public void AddFlowHotReload_RegistersAllServices()
    {
        var services = new ServiceCollection();

        services.AddFlowHotReload();

        var provider = services.BuildServiceProvider();
        provider.GetService<IFlowRegistry>().Should().NotBeNull();
        provider.GetService<IFlowVersionManager>().Should().NotBeNull();
        provider.GetService<IFlowReloader>().Should().NotBeNull();
    }

    [Fact]
    public void AddFlowHotReload_RegistersSingletons()
    {
        var services = new ServiceCollection();
        services.AddFlowHotReload();

        var provider = services.BuildServiceProvider();
        var registry1 = provider.GetService<IFlowRegistry>();
        var registry2 = provider.GetService<IFlowRegistry>();

        registry1.Should().BeSameAs(registry2);
    }

    [Fact]
    public void AddFlowHotReload_FlowReloader_UsesRegisteredServices()
    {
        var services = new ServiceCollection();
        services.AddFlowHotReload();

        var provider = services.BuildServiceProvider();
        var reloader = provider.GetRequiredService<IFlowReloader>();

        reloader.Should().NotBeNull();
    }

    [Fact]
    public void FlowRegistration_Record_HasFlowName()
    {
        var registration = new FlowRegistration("TestFlow");

        registration.FlowName.Should().Be("TestFlow");
    }

    [Fact]
    public void FlowRegistration_Equality_Works()
    {
        var reg1 = new FlowRegistration("TestFlow");
        var reg2 = new FlowRegistration("TestFlow");

        reg1.Should().Be(reg2);
    }
}
