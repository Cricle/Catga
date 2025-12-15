using FluentAssertions;

namespace Catga.Tests.Flow.HotReload;

/// <summary>
/// TDD tests for Flow Hot Reload functionality
/// </summary>
public class FlowHotReloadTests
{
    #region Interface Tests (Open-Closed Principle)

    [Fact]
    public void IFlowRegistry_ShouldExist()
    {
        var type = Type.GetType("Catga.Flow.HotReload.IFlowRegistry, Catga");

        type.Should().NotBeNull("IFlowRegistry interface should exist");
    }

    [Fact]
    public void IFlowRegistry_ShouldDefineRegistrationMethods()
    {
        var type = Type.GetType("Catga.Flow.HotReload.IFlowRegistry, Catga");

        type.Should().NotBeNull();
        type!.GetMethod("Register").Should().NotBeNull();
        type.GetMethod("Get").Should().NotBeNull();
        type.GetMethod("GetAll").Should().NotBeNull();
    }

    [Fact]
    public void IFlowReloader_ShouldExist()
    {
        var type = Type.GetType("Catga.Flow.HotReload.IFlowReloader, Catga");

        type.Should().NotBeNull("IFlowReloader interface should exist");
    }

    [Fact]
    public void IFlowReloader_ShouldDefineReloadMethods()
    {
        var type = Type.GetType("Catga.Flow.HotReload.IFlowReloader, Catga");

        type.Should().NotBeNull();
        type!.GetMethod("ReloadAsync").Should().NotBeNull();
    }

    #endregion

    #region Flow Registry Tests

    [Fact]
    public void FlowRegistry_ShouldExist()
    {
        var type = Type.GetType("Catga.Flow.HotReload.FlowRegistry, Catga");

        type.Should().NotBeNull("FlowRegistry class should exist");
    }

    #endregion

    #region Flow Version Manager Tests

    [Fact]
    public void IFlowVersionManager_ShouldExist()
    {
        var type = Type.GetType("Catga.Flow.HotReload.IFlowVersionManager, Catga");

        type.Should().NotBeNull("IFlowVersionManager interface should exist");
    }

    [Fact]
    public void IFlowVersionManager_ShouldDefineVersionMethods()
    {
        var type = Type.GetType("Catga.Flow.HotReload.IFlowVersionManager, Catga");

        type.Should().NotBeNull();
        type!.GetMethod("GetCurrentVersion").Should().NotBeNull();
        type.GetMethod("SetVersion").Should().NotBeNull();
    }

    #endregion

    #region Flow Reload Event Tests

    [Fact]
    public void FlowReloadedEvent_ShouldExist()
    {
        var type = Type.GetType("Catga.Flow.HotReload.FlowReloadedEvent, Catga");

        type.Should().NotBeNull("FlowReloadedEvent should exist");
    }

    #endregion
}
