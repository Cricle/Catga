using Catga.Flow.HotReload;
using FluentAssertions;

namespace Catga.Tests.Flow.HotReload;

/// <summary>
/// Advanced tests for Flow Hot Reload components
/// </summary>
public class FlowHotReloadAdvancedTests
{
    #region FlowRegistry Advanced Tests

    [Fact]
    public void FlowRegistry_Register_OverwritesExisting()
    {
        var registry = new FlowRegistry();
        var config1 = new object();
        var config2 = new object();

        registry.Register("TestFlow", config1);
        registry.Register("TestFlow", config2);

        registry.Get("TestFlow").Should().BeSameAs(config2);
    }

    [Fact]
    public void FlowRegistry_Register_ThrowsOnNullName()
    {
        var registry = new FlowRegistry();

        var act = () => registry.Register(null!, new object());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FlowRegistry_Register_ThrowsOnEmptyName()
    {
        var registry = new FlowRegistry();

        var act = () => registry.Register("", new object());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FlowRegistry_Register_ThrowsOnNullConfig()
    {
        var registry = new FlowRegistry();

        var act = () => registry.Register("TestFlow", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FlowRegistry_Unregister_NonExistent_ReturnsFalse()
    {
        var registry = new FlowRegistry();

        var result = registry.Unregister("NonExistent");

        result.Should().BeFalse();
    }

    [Fact]
    public void FlowRegistry_Contains_NonExistent_ReturnsFalse()
    {
        var registry = new FlowRegistry();

        registry.Contains("NonExistent").Should().BeFalse();
    }

    [Fact]
    public void FlowRegistry_GetAll_EmptyRegistry_ReturnsEmpty()
    {
        var registry = new FlowRegistry();

        registry.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void FlowRegistry_ConcurrentAccess_ShouldBeThreadSafe()
    {
        var registry = new FlowRegistry();
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() => registry.Register($"Flow{index}", new object())));
        }

        Task.WaitAll(tasks.ToArray());

        registry.GetAll().Count().Should().Be(100);
    }

    #endregion

    #region FlowVersionManager Advanced Tests

    [Fact]
    public void FlowVersionManager_SetVersion_OverwritesExisting()
    {
        var manager = new FlowVersionManager();

        manager.SetVersion("TestFlow", 5);
        manager.SetVersion("TestFlow", 10);

        manager.GetCurrentVersion("TestFlow").Should().Be(10);
    }

    [Fact]
    public void FlowVersionManager_IncrementVersion_StartsFromOne()
    {
        var manager = new FlowVersionManager();

        var version = manager.IncrementVersion("NewFlow");

        version.Should().Be(1);
    }

    [Fact]
    public void FlowVersionManager_MultipleFlows_IndependentVersions()
    {
        var manager = new FlowVersionManager();

        manager.IncrementVersion("Flow1");
        manager.IncrementVersion("Flow1");
        manager.IncrementVersion("Flow2");

        manager.GetCurrentVersion("Flow1").Should().Be(2);
        manager.GetCurrentVersion("Flow2").Should().Be(1);
    }

    [Fact]
    public void FlowVersionManager_ConcurrentIncrement_ShouldBeThreadSafe()
    {
        var manager = new FlowVersionManager();
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => manager.IncrementVersion("TestFlow")));
        }

        Task.WaitAll(tasks.ToArray());

        manager.GetCurrentVersion("TestFlow").Should().Be(100);
    }

    #endregion

    #region FlowReloader Advanced Tests

    [Fact]
    public async Task FlowReloader_MultipleReloads_IncrementsVersionCorrectly()
    {
        var registry = new FlowRegistry();
        var versionManager = new FlowVersionManager();
        var reloader = new FlowReloader(registry, versionManager);

        await reloader.ReloadAsync("TestFlow", new object());
        await reloader.ReloadAsync("TestFlow", new object());
        await reloader.ReloadAsync("TestFlow", new object());

        versionManager.GetCurrentVersion("TestFlow").Should().Be(3);
    }

    [Fact]
    public async Task FlowReloader_Event_ContainsCorrectVersions()
    {
        var registry = new FlowRegistry();
        var versionManager = new FlowVersionManager();
        var reloader = new FlowReloader(registry, versionManager);

        var events = new List<FlowReloadedEvent>();
        reloader.FlowReloaded += (_, e) => events.Add(e);

        await reloader.ReloadAsync("TestFlow", new object());
        await reloader.ReloadAsync("TestFlow", new object());

        events.Should().HaveCount(2);
        events[0].OldVersion.Should().Be(0);
        events[0].NewVersion.Should().Be(1);
        events[1].OldVersion.Should().Be(1);
        events[1].NewVersion.Should().Be(2);
    }

    [Fact]
    public async Task FlowReloader_Event_ContainsReloadedAtTime()
    {
        var registry = new FlowRegistry();
        var versionManager = new FlowVersionManager();
        var reloader = new FlowReloader(registry, versionManager);

        FlowReloadedEvent? receivedEvent = null;
        reloader.FlowReloaded += (_, e) => receivedEvent = e;

        var before = DateTime.UtcNow;
        await reloader.ReloadAsync("TestFlow", new object());
        var after = DateTime.UtcNow;

        receivedEvent!.ReloadedAt.Should().BeOnOrAfter(before);
        receivedEvent.ReloadedAt.Should().BeOnOrBefore(after);
    }

    [Fact]
    public async Task FlowReloader_NoEventHandler_ShouldNotThrow()
    {
        var registry = new FlowRegistry();
        var versionManager = new FlowVersionManager();
        var reloader = new FlowReloader(registry, versionManager);

        var act = async () => await reloader.ReloadAsync("TestFlow", new object());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FlowReloader_WithCancellation_ShouldThrow()
    {
        var registry = new FlowRegistry();
        var versionManager = new FlowVersionManager();
        var reloader = new FlowReloader(registry, versionManager);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await reloader.ReloadAsync("TestFlow", new object(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region FlowReloadedEvent Tests

    [Fact]
    public void FlowReloadedEvent_Equality_SameValues_ShouldBeEqual()
    {
        var time = DateTime.UtcNow;
        var evt1 = new FlowReloadedEvent
        {
            FlowName = "TestFlow",
            OldVersion = 1,
            NewVersion = 2,
            ReloadedAt = time
        };
        var evt2 = new FlowReloadedEvent
        {
            FlowName = "TestFlow",
            OldVersion = 1,
            NewVersion = 2,
            ReloadedAt = time
        };

        evt1.Should().Be(evt2);
    }

    [Fact]
    public void FlowReloadedEvent_ToString_ShouldNotThrow()
    {
        var evt = new FlowReloadedEvent
        {
            FlowName = "TestFlow",
            OldVersion = 1,
            NewVersion = 2,
            ReloadedAt = DateTime.UtcNow
        };

        var act = () => evt.ToString();

        act.Should().NotThrow();
    }

    #endregion
}
