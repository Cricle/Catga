using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.EventSourcing;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// E2E tests verifying that all persistence providers register the same services.
/// Ensures feature parity across InMemory, Redis, and NATS providers.
/// </summary>
public class PersistenceProviderE2ETests
{
    [Fact]
    public void UseInMemory_RegistersAllRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();

        // Assert - Core stores
        sp.GetService<IEventStore>().Should().NotBeNull("IEventStore should be registered");
        sp.GetService<ISnapshotStore>().Should().NotBeNull("ISnapshotStore should be registered");
        sp.GetService<IEnhancedSnapshotStore>().Should().NotBeNull("IEnhancedSnapshotStore should be registered");

        // Assert - Messaging stores
        sp.GetService<Catga.Inbox.IInboxStore>().Should().NotBeNull("IInboxStore should be registered");
        sp.GetService<Catga.Outbox.IOutboxStore>().Should().NotBeNull("IOutboxStore should be registered");
        sp.GetService<Catga.DeadLetter.IDeadLetterQueue>().Should().NotBeNull("IDeadLetterQueue should be registered");
        sp.GetService<Catga.Idempotency.IIdempotencyStore>().Should().NotBeNull("IIdempotencyStore should be registered");

        // Assert - Flow stores
        sp.GetService<Catga.Flow.IFlowStore>().Should().NotBeNull("IFlowStore should be registered");
        sp.GetService<IDslFlowStore>().Should().NotBeNull("IDslFlowStore should be registered");

        // Assert - Event sourcing advanced
        sp.GetService<IProjectionCheckpointStore>().Should().NotBeNull("IProjectionCheckpointStore should be registered");
        sp.GetService<ISubscriptionStore>().Should().NotBeNull("ISubscriptionStore should be registered");

        // Assert - Resilience
        sp.GetService<IResiliencePipelineProvider>().Should().NotBeNull("IResiliencePipelineProvider should be registered");

        // Assert - Event sourcing core
        sp.GetService<IEventTypeRegistry>().Should().NotBeNull("IEventTypeRegistry should be registered");
        sp.GetService<IEventVersionRegistry>().Should().NotBeNull("IEventVersionRegistry should be registered");
    }

    [Fact]
    public void UseInMemory_CanResolveAndUseEventStore()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();
        var sp = services.BuildServiceProvider();

        // Act
        var eventStore = sp.GetRequiredService<IEventStore>();
        var testEvent = new TestOrderCreated { OrderId = "ORD-001", Amount = 100m };

        // Assert - can append events
        var appendTask = eventStore.AppendAsync("test-stream", new[] { testEvent });
        appendTask.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void UseInMemory_CanResolveAndUseDslFlowStore()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();
        var sp = services.BuildServiceProvider();

        // Act
        var flowStore = sp.GetRequiredService<IDslFlowStore>();

        // Assert
        flowStore.Should().NotBeNull();
    }

    [Fact]
    public void UseInMemory_CanResolveAndUseProjectionCheckpointStore()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();
        var sp = services.BuildServiceProvider();

        // Act
        var checkpointStore = sp.GetRequiredService<IProjectionCheckpointStore>();

        // Assert
        checkpointStore.Should().NotBeNull();
    }

    [Fact]
    public async Task UseInMemory_EventStore_AppendAndRead_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();
        var sp = services.BuildServiceProvider();

        var eventStore = sp.GetRequiredService<IEventStore>();
        var streamId = $"test-stream-{Guid.NewGuid():N}";
        var events = new IEvent[]
        {
            new TestOrderCreated { OrderId = "ORD-001", Amount = 100m },
            new TestOrderUpdated { OrderId = "ORD-001", NewAmount = 150m }
        };

        // Act
        await eventStore.AppendAsync(streamId, events);
        var stream = await eventStore.ReadAsync(streamId);

        // Assert
        stream.Events.Should().HaveCount(2);
        stream.Events[0].Event.Should().BeOfType<TestOrderCreated>();
        stream.Events[1].Event.Should().BeOfType<TestOrderUpdated>();
    }

    [Fact]
    public async Task UseInMemory_SubscriptionStore_SaveAndList_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();
        var sp = services.BuildServiceProvider();

        var subscriptionStore = sp.GetRequiredService<ISubscriptionStore>();
        var subscription = new PersistentSubscription("order-events", "OrderAggregate-*");

        // Act
        await subscriptionStore.SaveAsync(subscription);
        var subscriptions = await subscriptionStore.ListAsync();

        // Assert
        subscriptions.Should().HaveCount(1);
        subscriptions[0].Name.Should().Be("order-events");
        subscriptions[0].StreamPattern.Should().Be("OrderAggregate-*");
    }

    #region Test Events

    public record TestOrderCreated : IEvent
    {
        public long MessageId { get; init; }
        public string OrderId { get; init; } = "";
        public decimal Amount { get; init; }
    }

    public record TestOrderUpdated : IEvent
    {
        public long MessageId { get; init; }
        public string OrderId { get; init; } = "";
        public decimal NewAmount { get; init; }
    }

    #endregion
}
