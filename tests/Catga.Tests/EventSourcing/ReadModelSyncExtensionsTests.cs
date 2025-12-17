using Catga.EventSourcing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Tests for ReadModelSyncExtensions
/// </summary>
public class ReadModelSyncExtensionsTests
{
    [Fact]
    public void AddReadModelSync_RegistersAllServices()
    {
        var services = new ServiceCollection();

        services.AddReadModelSync();

        var provider = services.BuildServiceProvider();
        provider.GetService<IChangeTracker>().Should().NotBeNull();
        provider.GetService<ISyncStrategy>().Should().NotBeNull();
        provider.GetService<IReadModelSynchronizer>().Should().NotBeNull();
    }

    [Fact]
    public void AddReadModelSync_RegistersSingletons()
    {
        var services = new ServiceCollection();
        services.AddReadModelSync();

        var provider = services.BuildServiceProvider();
        var sync1 = provider.GetService<IReadModelSynchronizer>();
        var sync2 = provider.GetService<IReadModelSynchronizer>();

        sync1.Should().BeSameAs(sync2);
    }

    [Fact]
    public void AddReadModelSyncWithBatching_RegistersBatchStrategy()
    {
        var services = new ServiceCollection();

        services.AddReadModelSyncWithBatching(10, _ => ValueTask.CompletedTask);

        var provider = services.BuildServiceProvider();
        var strategy = provider.GetService<ISyncStrategy>();

        strategy.Should().NotBeNull();
        strategy.Should().BeOfType<BatchSyncStrategy>();
    }

    [Fact]
    public void AddReadModelSyncWithSchedule_RegistersScheduledStrategy()
    {
        var services = new ServiceCollection();

        services.AddReadModelSyncWithSchedule(TimeSpan.FromMinutes(1), _ => ValueTask.CompletedTask);

        var provider = services.BuildServiceProvider();
        var strategy = provider.GetService<ISyncStrategy>();

        strategy.Should().NotBeNull();
        strategy.Should().BeOfType<ScheduledSyncStrategy>();
    }

    [Fact]
    public void AddReadModelSync_ChangeTracker_IsInMemory()
    {
        var services = new ServiceCollection();
        services.AddReadModelSync();

        var provider = services.BuildServiceProvider();
        var tracker = provider.GetService<IChangeTracker>();

        tracker.Should().BeOfType<InMemoryChangeTracker>();
    }

    [Fact]
    public void AddReadModelSync_Synchronizer_IsDefault()
    {
        var services = new ServiceCollection();
        services.AddReadModelSync();

        var provider = services.BuildServiceProvider();
        var synchronizer = provider.GetService<IReadModelSynchronizer>();

        synchronizer.Should().BeOfType<DefaultReadModelSynchronizer>();
    }
}
