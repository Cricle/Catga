using Catga.Abstractions;
using Catga.DeadLetter;
using Catga.DependencyInjection;
using Catga.EventSourcing;
using Catga.Inbox;
using Catga.Outbox;
using Catga.Persistence.Stores;
using Catga.Serialization.MemoryPack;
using Catga.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.DependencyInjection;

public class InMemoryPersistenceServiceCollectionExtensionsTests
{
    private IServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();
        // Add required dependencies
        services.AddSingleton<IMessageSerializer, MemoryPackMessageSerializer>();
        services.AddSingleton<IResiliencePipelineProvider, DiagnosticResiliencePipelineProvider>();
        services.AddLogging();
        return services;
    }

    [Fact]
    public void AddInMemoryEventStore_RegistersEventStore()
    {
        // Arrange
        var services = CreateServiceCollection();

        // Act
        services.AddInMemoryEventStore();
        var provider = services.BuildServiceProvider();

        // Assert
        var eventStore = provider.GetService<IEventStore>();
        Assert.NotNull(eventStore);
        Assert.IsType<InMemoryEventStore>(eventStore);
    }

    [Fact]
    public void AddInMemoryOutboxStore_RegistersOutboxStore()
    {
        // Arrange
        var services = CreateServiceCollection();

        // Act
        services.AddInMemoryOutboxStore();
        var provider = services.BuildServiceProvider();

        // Assert
        var outboxStore = provider.GetService<IOutboxStore>();
        Assert.NotNull(outboxStore);
        Assert.IsType<MemoryOutboxStore>(outboxStore);
    }

    [Fact]
    public void AddInMemoryInboxStore_RegistersInboxStore()
    {
        // Arrange
        var services = CreateServiceCollection();

        // Act
        services.AddInMemoryInboxStore();
        var provider = services.BuildServiceProvider();

        // Assert
        var inboxStore = provider.GetService<IInboxStore>();
        Assert.NotNull(inboxStore);
        Assert.IsType<MemoryInboxStore>(inboxStore);
    }

    [Fact]
    public void AddInMemoryDeadLetterQueue_RegistersDeadLetterQueue()
    {
        // Arrange
        var services = CreateServiceCollection();

        // Act
        services.AddInMemoryDeadLetterQueue(maxSize: 500);
        var provider = services.BuildServiceProvider();

        // Assert
        var dlq = provider.GetService<IDeadLetterQueue>();
        Assert.NotNull(dlq);
        Assert.IsType<InMemoryDeadLetterQueue>(dlq);
    }

    [Fact]
    public void AddInMemoryPersistence_RegistersAllStores()
    {
        // Arrange
        var services = CreateServiceCollection();

        // Act
        services.AddInMemoryPersistence(deadLetterMaxSize: 500);
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetService<IEventStore>());
        Assert.NotNull(provider.GetService<IOutboxStore>());
        Assert.NotNull(provider.GetService<IInboxStore>());
        Assert.NotNull(provider.GetService<IDeadLetterQueue>());

        Assert.IsType<InMemoryEventStore>(provider.GetService<IEventStore>());
        Assert.IsType<MemoryOutboxStore>(provider.GetService<IOutboxStore>());
        Assert.IsType<MemoryInboxStore>(provider.GetService<IInboxStore>());
        Assert.IsType<InMemoryDeadLetterQueue>(provider.GetService<IDeadLetterQueue>());
    }

    [Fact]
    public void AddInMemoryPersistence_RegistersAsSingleton()
    {
        // Arrange
        var services = CreateServiceCollection();
        services.AddInMemoryPersistence();
        var provider = services.BuildServiceProvider();

        // Act
        var eventStore1 = provider.GetService<IEventStore>();
        var eventStore2 = provider.GetService<IEventStore>();

        // Assert
        Assert.Same(eventStore1, eventStore2);
    }

    [Fact]
    public void AddInMemoryEventStore_ReturnsServiceCollection()
    {
        // Arrange
        var services = CreateServiceCollection();

        // Act
        var result = services.AddInMemoryEventStore();

        // Assert
        Assert.Same(services, result);
    }
}







