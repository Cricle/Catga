using Catga.DeadLetter;
using Catga.EventSourcing;
using Catga.Flow;
using Catga.Flow.Dsl;
using Catga.Idempotency;
using Catga.Inbox;
using Catga.Outbox;
using Catga.Persistence;
using FluentAssertions;
using Medallion.Threading;
using NSubstitute;

namespace Catga.Tests.Persistence;

public class IPersistenceProviderTests
{
    [Fact]
    public void Provider_ShouldHaveName()
    {
        var provider = Substitute.For<IPersistenceProvider>();
        provider.Name.Returns("TestProvider");

        provider.Name.Should().Be("TestProvider");
    }

    [Fact]
    public void Provider_CreateDslFlowStore_CanReturnNull()
    {
        var provider = Substitute.For<IPersistenceProvider>();
        provider.CreateDslFlowStore().Returns((IDslFlowStore?)null);

        provider.CreateDslFlowStore().Should().BeNull();
    }

    [Fact]
    public void Provider_CreateDslFlowStore_CanReturnStore()
    {
        var provider = Substitute.For<IPersistenceProvider>();
        var store = Substitute.For<IDslFlowStore>();
        provider.CreateDslFlowStore().Returns(store);

        provider.CreateDslFlowStore().Should().Be(store);
    }

    [Fact]
    public void Provider_AllMethodsReturnOptional()
    {
        var provider = Substitute.For<IPersistenceProvider>();

        // Configure substitute to return null explicitly
        provider.CreateDslFlowStore().Returns((IDslFlowStore?)null);
        provider.CreateOutboxStore().Returns((IOutboxStore?)null);
        provider.CreateInboxStore().Returns((IInboxStore?)null);
        provider.CreateEventStore().Returns((IEventStore?)null);
        provider.CreateIdempotencyStore().Returns((IIdempotencyStore?)null);
        provider.CreateDeadLetterQueue().Returns((IDeadLetterQueue?)null);
        provider.CreateSnapshotStore().Returns((ISnapshotStore?)null);
        provider.CreateDistributedLockProvider().Returns((IDistributedLockProvider?)null);
        provider.CreateFlowStore().Returns((IFlowStore?)null);
        provider.CreateProjectionCheckpointStore().Returns((IProjectionCheckpointStore?)null);

        provider.CreateDslFlowStore().Should().BeNull();
        provider.CreateOutboxStore().Should().BeNull();
        provider.CreateInboxStore().Should().BeNull();
        provider.CreateEventStore().Should().BeNull();
        provider.CreateIdempotencyStore().Should().BeNull();
        provider.CreateDeadLetterQueue().Should().BeNull();
        provider.CreateSnapshotStore().Should().BeNull();
        provider.CreateDistributedLockProvider().Should().BeNull();
        provider.CreateFlowStore().Should().BeNull();
        provider.CreateProjectionCheckpointStore().Should().BeNull();
    }
}

/// <summary>
/// Example implementation for testing
/// </summary>
public class TestPersistenceProvider : IPersistenceProvider
{
    public string Name => "Test";

    private readonly IDslFlowStore? _dslFlowStore;
    private readonly IOutboxStore? _outboxStore;

    public TestPersistenceProvider(IDslFlowStore? dslFlowStore = null, IOutboxStore? outboxStore = null)
    {
        _dslFlowStore = dslFlowStore;
        _outboxStore = outboxStore;
    }

    public IDslFlowStore? CreateDslFlowStore() => _dslFlowStore;
    public IOutboxStore? CreateOutboxStore() => _outboxStore;
    public IInboxStore? CreateInboxStore() => null;
    public IEventStore? CreateEventStore() => null;
    public IIdempotencyStore? CreateIdempotencyStore() => null;
    public IDeadLetterQueue? CreateDeadLetterQueue() => null;
    public ISnapshotStore? CreateSnapshotStore() => null;
    public IDistributedLockProvider? CreateDistributedLockProvider() => null;
    public IFlowStore? CreateFlowStore() => null;
    public IProjectionCheckpointStore? CreateProjectionCheckpointStore() => null;
}

public class TestPersistenceProviderTests
{
    [Fact]
    public void TestProvider_Name_ReturnsTest()
    {
        var provider = new TestPersistenceProvider();
        provider.Name.Should().Be("Test");
    }

    [Fact]
    public void TestProvider_WithDslFlowStore_ReturnsStore()
    {
        var store = Substitute.For<IDslFlowStore>();
        var provider = new TestPersistenceProvider(dslFlowStore: store);

        provider.CreateDslFlowStore().Should().Be(store);
    }

    [Fact]
    public void TestProvider_WithoutDslFlowStore_ReturnsNull()
    {
        var provider = new TestPersistenceProvider();
        provider.CreateDslFlowStore().Should().BeNull();
    }
}
