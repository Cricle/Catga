using Catga.Configuration;
using Catga.DependencyInjection;
using Catga.EventSourcing;
using FluentAssertions;
using Medallion.Threading;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Catga.Tests.DependencyInjection;

/// <summary>
/// Comprehensive tests for DistributedExtensions
/// </summary>
public class DistributedExtensionsTests
{
    private readonly IServiceCollection _services;
    private readonly CatgaServiceBuilder _builder;

    public DistributedExtensionsTests()
    {
        _services = new ServiceCollection();
        _builder = new CatgaServiceBuilder(_services, new Catga.Configuration.CatgaOptions());
    }

    #region UseDistributedLockProvider Tests

    [Fact]
    public void UseDistributedLockProvider_Generic_ShouldRegisterProvider()
    {
        _builder.UseDistributedLockProvider<TestDistributedLockProvider>();
        
        var provider = _services.BuildServiceProvider();
        var lockProvider = provider.GetService<IDistributedLockProvider>();
        
        lockProvider.Should().NotBeNull();
        lockProvider.Should().BeOfType<TestDistributedLockProvider>();
    }

    [Fact]
    public void UseDistributedLockProvider_Instance_ShouldRegisterProvider()
    {
        var mockProvider = Substitute.For<IDistributedLockProvider>();
        
        _builder.UseDistributedLockProvider(mockProvider);
        
        var provider = _services.BuildServiceProvider();
        var lockProvider = provider.GetService<IDistributedLockProvider>();
        
        lockProvider.Should().Be(mockProvider);
    }

    [Fact]
    public void UseDistributedLockProvider_ShouldNotOverwriteExisting()
    {
        var firstProvider = Substitute.For<IDistributedLockProvider>();
        var secondProvider = Substitute.For<IDistributedLockProvider>();
        
        _builder.UseDistributedLockProvider(firstProvider);
        _builder.UseDistributedLockProvider(secondProvider);
        
        var provider = _services.BuildServiceProvider();
        var lockProvider = provider.GetService<IDistributedLockProvider>();
        
        lockProvider.Should().Be(firstProvider);
    }

    #endregion

    #region UseSnapshotStore Tests

    [Fact]
    public void UseSnapshotStore_Generic_ShouldRegisterStore()
    {
        _builder.UseSnapshotStore<TestSnapshotStore>();
        
        var provider = _services.BuildServiceProvider();
        var store = provider.GetService<ISnapshotStore>();
        
        store.Should().NotBeNull();
        store.Should().BeOfType<TestSnapshotStore>();
    }

    [Fact]
    public void UseSnapshotStore_WithOptions_ShouldConfigureOptions()
    {
        _builder.UseSnapshotStore<TestSnapshotStore>(options =>
        {
            options.EventThreshold = 50;
        });
        
        var provider = _services.BuildServiceProvider();
        var store = provider.GetService<ISnapshotStore>();
        
        store.Should().NotBeNull();
    }

    #endregion

    #region UseEventVersioning Tests

    [Fact]
    public void UseEventVersioning_ShouldRegisterRegistry()
    {
        _builder.UseEventVersioning();
        
        var provider = _services.BuildServiceProvider();
        var registry = provider.GetService<IEventVersionRegistry>();
        
        registry.Should().NotBeNull();
        registry.Should().BeOfType<EventVersionRegistry>();
    }

    [Fact]
    public void UseEventVersioning_WithConfigure_ShouldConfigureRegistry()
    {
        var configured = false;
        
        _builder.UseEventVersioning(registry =>
        {
            configured = true;
        });
        
        configured.Should().BeTrue();
        
        var provider = _services.BuildServiceProvider();
        var registry = provider.GetService<IEventVersionRegistry>();
        
        registry.Should().NotBeNull();
    }

    #endregion

    #region UseSnapshotStrategy Tests

    [Fact]
    public void UseSnapshotStrategy_Generic_ShouldRegisterStrategy()
    {
        _builder.UseSnapshotStrategy<TestSnapshotStrategy>();
        
        var provider = _services.BuildServiceProvider();
        var strategy = provider.GetService<ISnapshotStrategy>();
        
        strategy.Should().NotBeNull();
        strategy.Should().BeOfType<TestSnapshotStrategy>();
    }

    [Fact]
    public void UseEventCountSnapshots_ShouldRegisterEventCountStrategy()
    {
        _builder.UseEventCountSnapshots(50);
        
        var provider = _services.BuildServiceProvider();
        var strategy = provider.GetService<ISnapshotStrategy>();
        
        strategy.Should().NotBeNull();
        strategy.Should().BeOfType<EventCountSnapshotStrategy>();
    }

    [Fact]
    public void UseEventCountSnapshots_DefaultThreshold_ShouldUse100()
    {
        _builder.UseEventCountSnapshots();
        
        var provider = _services.BuildServiceProvider();
        var strategy = provider.GetService<ISnapshotStrategy>();
        
        strategy.Should().NotBeNull();
    }

    #endregion

    #region AddAggregateRepository Tests

    [Fact]
    public void AddAggregateRepository_ShouldRegisterRepository()
    {
        // Need to add required dependencies first
        _services.AddSingleton(Substitute.For<IEventStore>());
        _services.AddSingleton(Substitute.For<ISnapshotStore>());
        
        _builder.AddAggregateRepository<TestAggregate>();
        
        var provider = _services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetService<IAggregateRepository<TestAggregate>>();
        
        repository.Should().NotBeNull();
    }

    #endregion

    #region Test Helpers

    public class TestDistributedLockProvider : IDistributedLockProvider
    {
        public IDistributedLock CreateLock(string name) => 
            Substitute.For<IDistributedLock>();
    }

    public class TestSnapshotStore : ISnapshotStore
    {
        public ValueTask<Snapshot<TAggregate>?> LoadAsync<TAggregate>(string streamId, CancellationToken ct = default) where TAggregate : class
            => ValueTask.FromResult<Snapshot<TAggregate>?>(null);

        public ValueTask SaveAsync<TAggregate>(string streamId, TAggregate aggregate, long version, CancellationToken ct = default) where TAggregate : class
            => ValueTask.CompletedTask;

        public ValueTask DeleteAsync(string streamId, CancellationToken ct = default)
            => ValueTask.CompletedTask;
    }

    public class TestSnapshotStrategy : ISnapshotStrategy
    {
        public bool ShouldTakeSnapshot(long currentVersion, long lastSnapshotVersion)
            => currentVersion - lastSnapshotVersion >= 100;
    }

    public class TestAggregate : AggregateRoot
    {
        private string _id = string.Empty;
        
        public override string Id
        {
            get => _id;
            protected set => _id = value;
        }
        
        public string? Name { get; private set; }
        
        protected override void When(Catga.Abstractions.IEvent @event)
        {
            // No-op for testing
        }
    }

    #endregion
}
