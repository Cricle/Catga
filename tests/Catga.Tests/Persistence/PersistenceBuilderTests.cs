using Catga.DeadLetter;
using Catga.EventSourcing;
using Catga.Flow;
using Catga.Flow.Dsl;
using Catga.Idempotency;
using Catga.Inbox;
using Catga.Locking;
using Catga.Outbox;
using Catga.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Catga.Tests.Persistence;

public class PersistenceBuilderTests
{
    private readonly IServiceCollection _services;
    private readonly IPersistenceProvider _provider;

    public PersistenceBuilderTests()
    {
        _services = new ServiceCollection();
        _provider = Substitute.For<IPersistenceProvider>();
        _provider.Name.Returns("TestProvider");
    }

    [Fact]
    public void AddDslFlowStore_WhenProviderReturnsStore_RegistersStore()
    {
        var store = Substitute.For<IDslFlowStore>();
        _provider.CreateDslFlowStore().Returns(store);

        _services.AddPersistence(_provider).AddDslFlowStore();

        var sp = _services.BuildServiceProvider();
        sp.GetService<IDslFlowStore>().Should().Be(store);
    }

    [Fact]
    public void AddDslFlowStore_WhenProviderReturnsNull_DoesNotRegister()
    {
        _provider.CreateDslFlowStore().Returns((IDslFlowStore?)null);

        _services.AddPersistence(_provider).AddDslFlowStore();

        var sp = _services.BuildServiceProvider();
        sp.GetService<IDslFlowStore>().Should().BeNull();
    }

    [Fact]
    public void AddOutbox_WhenProviderReturnsStore_RegistersStore()
    {
        var store = Substitute.For<IOutboxStore>();
        _provider.CreateOutboxStore().Returns(store);

        _services.AddPersistence(_provider).AddOutbox();

        var sp = _services.BuildServiceProvider();
        sp.GetService<IOutboxStore>().Should().Be(store);
    }

    [Fact]
    public void AddInbox_WhenProviderReturnsStore_RegistersStore()
    {
        var store = Substitute.For<IInboxStore>();
        _provider.CreateInboxStore().Returns(store);

        _services.AddPersistence(_provider).AddInbox();

        var sp = _services.BuildServiceProvider();
        sp.GetService<IInboxStore>().Should().Be(store);
    }

    [Fact]
    public void AddEventStore_WhenProviderReturnsStore_RegistersStore()
    {
        var store = Substitute.For<IEventStore>();
        _provider.CreateEventStore().Returns(store);

        _services.AddPersistence(_provider).AddEventStore();

        var sp = _services.BuildServiceProvider();
        sp.GetService<IEventStore>().Should().Be(store);
    }

    [Fact]
    public void AddIdempotency_WhenProviderReturnsStore_RegistersStore()
    {
        var store = Substitute.For<IIdempotencyStore>();
        _provider.CreateIdempotencyStore().Returns(store);

        _services.AddPersistence(_provider).AddIdempotency();

        var sp = _services.BuildServiceProvider();
        sp.GetService<IIdempotencyStore>().Should().Be(store);
    }

    [Fact]
    public void AddDeadLetterQueue_WhenProviderReturnsStore_RegistersStore()
    {
        var store = Substitute.For<IDeadLetterQueue>();
        _provider.CreateDeadLetterQueue().Returns(store);

        _services.AddPersistence(_provider).AddDeadLetterQueue();

        var sp = _services.BuildServiceProvider();
        sp.GetService<IDeadLetterQueue>().Should().Be(store);
    }

    [Fact]
    public void AddSnapshotStore_WhenProviderReturnsStore_RegistersStore()
    {
        var store = Substitute.For<ISnapshotStore>();
        _provider.CreateSnapshotStore().Returns(store);

        _services.AddPersistence(_provider).AddSnapshotStore();

        var sp = _services.BuildServiceProvider();
        sp.GetService<ISnapshotStore>().Should().Be(store);
    }

    [Fact]
    public void AddDistributedLock_WhenProviderReturnsProvider_RegistersProvider()
    {
        var lockProvider = Substitute.For<IDistributedLockProvider>();
        _provider.CreateDistributedLockProvider().Returns(lockProvider);

        _services.AddPersistence(_provider).AddDistributedLock();

        var sp = _services.BuildServiceProvider();
        sp.GetService<IDistributedLockProvider>().Should().Be(lockProvider);
    }

    [Fact]
    public void AddFlowStore_WhenProviderReturnsStore_RegistersStore()
    {
        var store = Substitute.For<IFlowStore>();
        _provider.CreateFlowStore().Returns(store);

        _services.AddPersistence(_provider).AddFlowStore();

        var sp = _services.BuildServiceProvider();
        sp.GetService<IFlowStore>().Should().Be(store);
    }

    [Fact]
    public void AddProjectionCheckpoint_WhenProviderReturnsStore_RegistersStore()
    {
        var store = Substitute.For<IProjectionCheckpointStore>();
        _provider.CreateProjectionCheckpointStore().Returns(store);

        _services.AddPersistence(_provider).AddProjectionCheckpoint();

        var sp = _services.BuildServiceProvider();
        sp.GetService<IProjectionCheckpointStore>().Should().Be(store);
    }

    [Fact]
    public void AddAll_RegistersAllAvailableStores()
    {
        var dslFlowStore = Substitute.For<IDslFlowStore>();
        var outboxStore = Substitute.For<IOutboxStore>();
        var inboxStore = Substitute.For<IInboxStore>();
        var eventStore = Substitute.For<IEventStore>();
        var idempotencyStore = Substitute.For<IIdempotencyStore>();
        var dlq = Substitute.For<IDeadLetterQueue>();
        var snapshotStore = Substitute.For<ISnapshotStore>();
        var lockProvider = Substitute.For<IDistributedLockProvider>();
        var flowStore = Substitute.For<IFlowStore>();
        var checkpointStore = Substitute.For<IProjectionCheckpointStore>();

        _provider.CreateDslFlowStore().Returns(dslFlowStore);
        _provider.CreateOutboxStore().Returns(outboxStore);
        _provider.CreateInboxStore().Returns(inboxStore);
        _provider.CreateEventStore().Returns(eventStore);
        _provider.CreateIdempotencyStore().Returns(idempotencyStore);
        _provider.CreateDeadLetterQueue().Returns(dlq);
        _provider.CreateSnapshotStore().Returns(snapshotStore);
        _provider.CreateDistributedLockProvider().Returns(lockProvider);
        _provider.CreateFlowStore().Returns(flowStore);
        _provider.CreateProjectionCheckpointStore().Returns(checkpointStore);

        _services.AddPersistence(_provider).AddAll();

        var sp = _services.BuildServiceProvider();
        sp.GetService<IDslFlowStore>().Should().Be(dslFlowStore);
        sp.GetService<IOutboxStore>().Should().Be(outboxStore);
        sp.GetService<IInboxStore>().Should().Be(inboxStore);
        sp.GetService<IEventStore>().Should().Be(eventStore);
        sp.GetService<IIdempotencyStore>().Should().Be(idempotencyStore);
        sp.GetService<IDeadLetterQueue>().Should().Be(dlq);
        sp.GetService<ISnapshotStore>().Should().Be(snapshotStore);
        sp.GetService<IDistributedLockProvider>().Should().Be(lockProvider);
        sp.GetService<IFlowStore>().Should().Be(flowStore);
        sp.GetService<IProjectionCheckpointStore>().Should().Be(checkpointStore);
    }

    [Fact]
    public void FluentChaining_ReturnsBuilder()
    {
        var builder = _services.AddPersistence(_provider);

        var result = builder
            .AddDslFlowStore()
            .AddOutbox()
            .AddInbox()
            .AddEventStore();

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddPersistence_DoesNotRegisterDuplicates()
    {
        var store = Substitute.For<IDslFlowStore>();
        _provider.CreateDslFlowStore().Returns(store);

        _services.AddPersistence(_provider)
            .AddDslFlowStore()
            .AddDslFlowStore();

        _services.Count(s => s.ServiceType == typeof(IDslFlowStore)).Should().Be(1);
    }
}
