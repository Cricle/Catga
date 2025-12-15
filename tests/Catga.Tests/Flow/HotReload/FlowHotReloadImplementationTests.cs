using Catga.Flow.HotReload;
using FluentAssertions;

namespace Catga.Tests.Flow.HotReload;

/// <summary>
/// Implementation tests for Flow Hot Reload components
/// </summary>
public class FlowHotReloadImplementationTests
{
    #region FlowRegistry Tests

    [Fact]
    public void FlowRegistry_Register_AddsFlow()
    {
        var registry = new FlowRegistry();
        var config = new object();

        registry.Register("TestFlow", config);

        registry.Contains("TestFlow").Should().BeTrue();
    }

    [Fact]
    public void FlowRegistry_Get_ReturnsRegisteredFlow()
    {
        var registry = new FlowRegistry();
        var config = new object();
        registry.Register("TestFlow", config);

        var result = registry.Get("TestFlow");

        result.Should().BeSameAs(config);
    }

    [Fact]
    public void FlowRegistry_Get_ReturnsNullForUnknownFlow()
    {
        var registry = new FlowRegistry();

        var result = registry.Get("Unknown");

        result.Should().BeNull();
    }

    [Fact]
    public void FlowRegistry_GetAll_ReturnsAllFlowNames()
    {
        var registry = new FlowRegistry();
        registry.Register("Flow1", new object());
        registry.Register("Flow2", new object());

        var all = registry.GetAll().ToList();

        all.Should().HaveCount(2);
        all.Should().Contain("Flow1");
        all.Should().Contain("Flow2");
    }

    [Fact]
    public void FlowRegistry_Unregister_RemovesFlow()
    {
        var registry = new FlowRegistry();
        registry.Register("TestFlow", new object());

        var removed = registry.Unregister("TestFlow");

        removed.Should().BeTrue();
        registry.Contains("TestFlow").Should().BeFalse();
    }

    #endregion

    #region FlowVersionManager Tests

    [Fact]
    public void FlowVersionManager_GetCurrentVersion_ReturnsZeroForNew()
    {
        var manager = new FlowVersionManager();

        var version = manager.GetCurrentVersion("NewFlow");

        version.Should().Be(0);
    }

    [Fact]
    public void FlowVersionManager_SetVersion_SetsVersion()
    {
        var manager = new FlowVersionManager();

        manager.SetVersion("TestFlow", 5);

        manager.GetCurrentVersion("TestFlow").Should().Be(5);
    }

    [Fact]
    public void FlowVersionManager_IncrementVersion_IncrementsAndReturns()
    {
        var manager = new FlowVersionManager();

        var v1 = manager.IncrementVersion("TestFlow");
        var v2 = manager.IncrementVersion("TestFlow");

        v1.Should().Be(1);
        v2.Should().Be(2);
    }

    #endregion

    #region FlowReloader Tests

    [Fact]
    public async Task FlowReloader_ReloadAsync_UpdatesRegistry()
    {
        var registry = new FlowRegistry();
        var versionManager = new FlowVersionManager();
        var reloader = new FlowReloader(registry, versionManager);

        var newConfig = new object();
        await reloader.ReloadAsync("TestFlow", newConfig);

        registry.Get("TestFlow").Should().BeSameAs(newConfig);
    }

    [Fact]
    public async Task FlowReloader_ReloadAsync_IncrementsVersion()
    {
        var registry = new FlowRegistry();
        var versionManager = new FlowVersionManager();
        var reloader = new FlowReloader(registry, versionManager);

        await reloader.ReloadAsync("TestFlow", new object());
        await reloader.ReloadAsync("TestFlow", new object());

        versionManager.GetCurrentVersion("TestFlow").Should().Be(2);
    }

    [Fact]
    public async Task FlowReloader_ReloadAsync_RaisesEvent()
    {
        var registry = new FlowRegistry();
        var versionManager = new FlowVersionManager();
        var reloader = new FlowReloader(registry, versionManager);

        FlowReloadedEvent? receivedEvent = null;
        reloader.FlowReloaded += (_, e) => receivedEvent = e;

        await reloader.ReloadAsync("TestFlow", new object());

        receivedEvent.Should().NotBeNull();
        receivedEvent!.FlowName.Should().Be("TestFlow");
        receivedEvent.OldVersion.Should().Be(0);
        receivedEvent.NewVersion.Should().Be(1);
    }

    #endregion

    #region FlowReloadedEvent Tests

    [Fact]
    public void FlowReloadedEvent_CanBeCreated()
    {
        var evt = new FlowReloadedEvent
        {
            FlowName = "TestFlow",
            OldVersion = 1,
            NewVersion = 2,
            ReloadedAt = DateTime.UtcNow
        };

        evt.FlowName.Should().Be("TestFlow");
        evt.OldVersion.Should().Be(1);
        evt.NewVersion.Should().Be(2);
    }

    #endregion
}
