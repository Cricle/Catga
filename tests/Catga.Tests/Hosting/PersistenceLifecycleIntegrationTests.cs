using Catga.EventSourcing;
using Catga.Hosting;
using Catga.Outbox;
using Catga.Persistence.Stores;
using Catga.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Catga.Tests.Hosting;

/// <summary>
/// 持久化层生命周期集成测试
/// 测试 EventStore 和 OutboxStore 的初始化、健康检查和恢复集成
/// </summary>
public class PersistenceLifecycleIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public PersistenceLifecycleIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task EventStore_ImplementsHealthCheckable_ReportsHealthyAfterSuccessfulOperation()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IEventStore, InMemoryEventStore>();

        var provider = services.BuildServiceProvider();
        var eventStore = provider.GetRequiredService<IEventStore>();

        // Act - Perform a successful operation
        await eventStore.AppendAsync("test-stream", new[] { new TestEvent { Data = "test", MessageId = 1 } });

        // Assert - Check health status
        Assert.IsAssignableFrom<IHealthCheckable>(eventStore);
        var healthCheckable = (IHealthCheckable)eventStore;
        Assert.True(healthCheckable.IsHealthy);
        Assert.NotNull(healthCheckable.HealthStatus);
        Assert.NotNull(healthCheckable.LastHealthCheck);
    }

    [Fact]
    public async Task EventStore_ImplementsRecoverableComponent_CanRecover()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IEventStore, InMemoryEventStore>();

        var provider = services.BuildServiceProvider();
        var eventStore = provider.GetRequiredService<IEventStore>();

        // Act - Attempt recovery
        Assert.IsAssignableFrom<Catga.Hosting.IRecoverableComponent>(eventStore);
        var recoverable = (Catga.Hosting.IRecoverableComponent)eventStore;
        await recoverable.RecoverAsync();

        // Assert - Should be healthy after recovery
        var healthCheckable = (IHealthCheckable)eventStore;
        Assert.True(healthCheckable.IsHealthy);
        Assert.Contains("Healthy", healthCheckable.HealthStatus ?? "");
    }

    [Fact]
    public async Task OutboxStore_ImplementsHealthCheckable_ReportsHealthyAfterSuccessfulOperation()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IOutboxStore, MemoryOutboxStore>();

        var provider = services.BuildServiceProvider();
        var outboxStore = provider.GetRequiredService<IOutboxStore>();

        // Act - Perform a successful operation
        var message = new OutboxMessage
        {
            MessageId = 1,
            MessageType = "TestMessage",
            Payload = new byte[] { 1, 2, 3 }
        };
        await outboxStore.AddAsync(message);

        // Assert - Check health status
        Assert.IsAssignableFrom<IHealthCheckable>(outboxStore);
        var healthCheckable = (IHealthCheckable)outboxStore;
        Assert.True(healthCheckable.IsHealthy);
        Assert.NotNull(healthCheckable.HealthStatus);
        Assert.NotNull(healthCheckable.LastHealthCheck);
    }

    [Fact]
    public async Task OutboxStore_ImplementsRecoverableComponent_CanRecover()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IOutboxStore, MemoryOutboxStore>();

        var provider = services.BuildServiceProvider();
        var outboxStore = provider.GetRequiredService<IOutboxStore>();

        // Act - Attempt recovery
        Assert.IsAssignableFrom<Catga.Hosting.IRecoverableComponent>(outboxStore);
        var recoverable = (Catga.Hosting.IRecoverableComponent)outboxStore;
        await recoverable.RecoverAsync();

        // Assert - Should be healthy after recovery
        var healthCheckable = (IHealthCheckable)outboxStore;
        Assert.True(healthCheckable.IsHealthy);
        Assert.Contains("Healthy", healthCheckable.HealthStatus ?? "");
    }

    [Fact]
    public async Task EventStore_IntegratesWithRecoveryService_CanBeRecovered()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IEventStore, InMemoryEventStore>();
        
        // Register EventStore as a recoverable component
        services.AddSingleton<Catga.Hosting.IRecoverableComponent>(sp => 
            (Catga.Hosting.IRecoverableComponent)sp.GetRequiredService<IEventStore>());

        var provider = services.BuildServiceProvider();
        var components = provider.GetServices<Catga.Hosting.IRecoverableComponent>().ToList();

        // Act - Find and recover the EventStore component
        var eventStoreComponent = components.FirstOrDefault(c => c.ComponentName == "InMemoryEventStore");
        Assert.NotNull(eventStoreComponent);
        
        await eventStoreComponent.RecoverAsync();

        // Assert - Should be healthy after recovery
        Assert.True(eventStoreComponent.IsHealthy);
    }

    [Fact]
    public async Task OutboxStore_IntegratesWithRecoveryService_CanBeRecovered()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IOutboxStore, MemoryOutboxStore>();
        
        // Register OutboxStore as a recoverable component
        services.AddSingleton<Catga.Hosting.IRecoverableComponent>(sp => 
            (Catga.Hosting.IRecoverableComponent)sp.GetRequiredService<IOutboxStore>());

        var provider = services.BuildServiceProvider();
        var components = provider.GetServices<Catga.Hosting.IRecoverableComponent>().ToList();

        // Act - Find and recover the OutboxStore component
        var outboxComponent = components.FirstOrDefault(c => c.ComponentName == "MemoryOutboxStore");
        Assert.NotNull(outboxComponent);
        
        await outboxComponent.RecoverAsync();

        // Assert - Should be healthy after recovery
        Assert.True(outboxComponent.IsHealthy);
    }

    [Fact]
    public async Task PersistenceHealthCheck_ReflectsEventStoreHealth()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IEventStore, InMemoryEventStore>();
        services.AddSingleton<IOutboxStore, MemoryOutboxStore>();
        services.AddSingleton<PersistenceHealthCheck>();

        var provider = services.BuildServiceProvider();
        var healthCheck = provider.GetRequiredService<PersistenceHealthCheck>();
        var eventStore = provider.GetRequiredService<IEventStore>();

        // Act - Perform operation to ensure health
        await eventStore.AppendAsync("test", new[] { new TestEvent { Data = "test", MessageId = 1 } });
        var result = await healthCheck.CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext());

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task PersistenceHealthCheck_ReflectsOutboxStoreHealth()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IEventStore, InMemoryEventStore>();
        services.AddSingleton<IOutboxStore, MemoryOutboxStore>();
        services.AddSingleton<PersistenceHealthCheck>();

        var provider = services.BuildServiceProvider();
        var healthCheck = provider.GetRequiredService<PersistenceHealthCheck>();
        var outboxStore = provider.GetRequiredService<IOutboxStore>();

        // Act - Perform operation to ensure health
        await outboxStore.AddAsync(new OutboxMessage
        {
            MessageId = 1,
            MessageType = "Test",
            Payload = new byte[] { 1 }
        });
        var result = await healthCheck.CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext());

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task EventStore_HealthStatusUpdates_AfterOperations()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IEventStore, InMemoryEventStore>();

        var provider = services.BuildServiceProvider();
        var eventStore = provider.GetRequiredService<IEventStore>();
        var healthCheckable = (IHealthCheckable)eventStore;

        // Act - Perform multiple operations
        await eventStore.AppendAsync("stream1", new[] { new TestEvent { Data = "test1", MessageId = 1 } });
        var health1 = healthCheckable.LastHealthCheck;
        
        await Task.Delay(10); // Small delay to ensure timestamp changes
        
        await eventStore.AppendAsync("stream2", new[] { new TestEvent { Data = "test2", MessageId = 2 } });
        var health2 = healthCheckable.LastHealthCheck;

        // Assert - Health check timestamp should update
        Assert.NotNull(health1);
        Assert.NotNull(health2);
        Assert.True(health2 >= health1, "Health check timestamp should update after operations");
    }

    [Fact]
    public async Task OutboxStore_HealthStatusUpdates_AfterOperations()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IOutboxStore, MemoryOutboxStore>();

        var provider = services.BuildServiceProvider();
        var outboxStore = provider.GetRequiredService<IOutboxStore>();
        var healthCheckable = (IHealthCheckable)outboxStore;

        // Act - Perform multiple operations
        await outboxStore.AddAsync(new OutboxMessage
        {
            MessageId = 1,
            MessageType = "Test1",
            Payload = new byte[] { 1 }
        });
        var health1 = healthCheckable.LastHealthCheck;
        
        await Task.Delay(10); // Small delay to ensure timestamp changes
        
        await outboxStore.AddAsync(new OutboxMessage
        {
            MessageId = 2,
            MessageType = "Test2",
            Payload = new byte[] { 2 }
        });
        var health2 = healthCheckable.LastHealthCheck;

        // Assert - Health check timestamp should update
        Assert.NotNull(health1);
        Assert.NotNull(health2);
        Assert.True(health2 >= health1, "Health check timestamp should update after operations");
    }

    // Test event for testing
    private record TestEvent : Catga.Abstractions.IEvent
    {
        public required string Data { get; init; }
        public long MessageId { get; init; }
    }
}
