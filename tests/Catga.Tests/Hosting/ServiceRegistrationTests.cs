using Catga.DependencyInjection;
using Catga.Hosting;
using Catga.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using System.Buffers;
using Xunit;

namespace Catga.Tests.Hosting;

/// <summary>
/// 服务注册单元测试
/// Tests: Requirements 9.1, 9.2, 9.3
/// </summary>
public class ServiceRegistrationTests
{
    /// <summary>
    /// 测试 AddHostedServices 注册所有必需的服务
    /// Requirements: 9.1, 9.2
    /// </summary>
    [Fact]
    public void AddHostedServices_RegistersAllRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostApplicationLifetime>(new TestApplicationLifetime());
        services.AddSingleton<IMessageTransport>(new TestMessageTransport());
        services.AddSingleton<Catga.Outbox.IOutboxStore>(new TestOutboxStore());
        services.AddSingleton<Catga.Abstractions.IMessageSerializer>(new TestMessageSerializer());
        
        var options = new Catga.Configuration.CatgaOptions();
        var builder = new CatgaServiceBuilder(services, options);
        
        // Act
        builder.AddHostedServices();
        var serviceProvider = services.BuildServiceProvider();
        
        // Assert
        var hostingOptions = serviceProvider.GetService<HostingOptions>();
        Assert.NotNull(hostingOptions);
        
        var hostedServices = serviceProvider.GetServices<IHostedService>().ToList();
        Assert.Contains(hostedServices, s => s is RecoveryHostedService);
        Assert.Contains(hostedServices, s => s is TransportHostedService);
        Assert.Contains(hostedServices, s => s is OutboxProcessorService);
        
        var recoveryOptions = serviceProvider.GetService<RecoveryOptions>();
        Assert.NotNull(recoveryOptions);
        
        var outboxOptions = serviceProvider.GetService<OutboxProcessorOptions>();
        Assert.NotNull(outboxOptions);
    }
    
    /// <summary>
    /// 测试 AddHostedServices 支持自定义配置
    /// Requirements: 9.1, 9.3
    /// </summary>
    [Fact]
    public void AddHostedServices_SupportsCustomConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostApplicationLifetime>(new TestApplicationLifetime());
        services.AddSingleton<IMessageTransport>(new TestMessageTransport());
        services.AddSingleton<Catga.Outbox.IOutboxStore>(new TestOutboxStore());
        services.AddSingleton<Catga.Abstractions.IMessageSerializer>(new TestMessageSerializer());
        
        var options = new Catga.Configuration.CatgaOptions();
        var builder = new CatgaServiceBuilder(services, options);
        
        // Act
        builder.AddHostedServices(hostingOptions =>
        {
            hostingOptions.EnableAutoRecovery = false;
            hostingOptions.ShutdownTimeout = TimeSpan.FromSeconds(60);
            hostingOptions.Recovery.CheckInterval = TimeSpan.FromSeconds(60);
            hostingOptions.OutboxProcessor.BatchSize = 200;
        });
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Assert
        var hostingOptions = serviceProvider.GetService<HostingOptions>();
        Assert.NotNull(hostingOptions);
        Assert.False(hostingOptions.EnableAutoRecovery);
        Assert.Equal(TimeSpan.FromSeconds(60), hostingOptions.ShutdownTimeout);
        Assert.Equal(TimeSpan.FromSeconds(60), hostingOptions.Recovery.CheckInterval);
        Assert.Equal(200, hostingOptions.OutboxProcessor.BatchSize);
        
        // RecoveryHostedService 不应该被注册
        var hostedServices = serviceProvider.GetServices<IHostedService>().ToList();
        Assert.DoesNotContain(hostedServices, s => s is RecoveryHostedService);
    }
    
    /// <summary>
    /// 测试 AddCatgaHealthChecks 注册所有健康检查
    /// Requirements: 9.1, 9.2
    /// </summary>
    [Fact]
    public void AddCatgaHealthChecks_RegistersAllHealthChecks()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMessageTransport>(new TestMessageTransport());
        
        var healthChecksBuilder = services.AddHealthChecks();
        
        // Act
        healthChecksBuilder.AddCatgaHealthChecks();
        var serviceProvider = services.BuildServiceProvider();
        
        // Assert
        var healthCheckService = serviceProvider.GetService<HealthCheckService>();
        Assert.NotNull(healthCheckService);
        
        // 验证健康检查已注册
        var healthCheckOptions = serviceProvider.GetService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        Assert.NotNull(healthCheckOptions);
        
        var registrations = healthCheckOptions.Value.Registrations;
        Assert.Contains(registrations, r => r.Name == "catga_transport");
        Assert.Contains(registrations, r => r.Name == "catga_persistence");
        Assert.Contains(registrations, r => r.Name == "catga_recovery");
    }
    
    /// <summary>
    /// 测试 AddHostingWithHealthChecks 便捷方法
    /// Requirements: 9.1, 9.3
    /// </summary>
    [Fact]
    public void AddHostingWithHealthChecks_RegistersBothHostingAndHealthChecks()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostApplicationLifetime>(new TestApplicationLifetime());
        services.AddSingleton<IMessageTransport>(new TestMessageTransport());
        services.AddSingleton<Catga.Outbox.IOutboxStore>(new TestOutboxStore());
        services.AddSingleton<Catga.Abstractions.IMessageSerializer>(new TestMessageSerializer());
        
        var options = new Catga.Configuration.CatgaOptions();
        var builder = new CatgaServiceBuilder(services, options);
        
        // Act
        builder.AddHostingWithHealthChecks();
        var serviceProvider = services.BuildServiceProvider();
        
        // Assert - 验证托管服务已注册
        var hostedServices = serviceProvider.GetServices<IHostedService>().ToList();
        Assert.Contains(hostedServices, s => s is RecoveryHostedService);
        Assert.Contains(hostedServices, s => s is TransportHostedService);
        Assert.Contains(hostedServices, s => s is OutboxProcessorService);
        
        // Assert - 验证健康检查已注册
        var healthCheckService = serviceProvider.GetService<HealthCheckService>();
        Assert.NotNull(healthCheckService);
        
        var healthCheckOptions = serviceProvider.GetService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        Assert.NotNull(healthCheckOptions);
        
        var registrations = healthCheckOptions.Value.Registrations;
        Assert.Contains(registrations, r => r.Name == "catga_transport");
        Assert.Contains(registrations, r => r.Name == "catga_persistence");
        Assert.Contains(registrations, r => r.Name == "catga_recovery");
    }
    
    /// <summary>
    /// 测试链式 API
    /// Requirements: 9.3
    /// </summary>
    [Fact]
    public void AddHostedServices_SupportsFluentAPI()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostApplicationLifetime>(new TestApplicationLifetime());
        services.AddSingleton<IMessageTransport>(new TestMessageTransport());
        services.AddSingleton<Catga.Outbox.IOutboxStore>(new TestOutboxStore());
        services.AddSingleton<Catga.Abstractions.IMessageSerializer>(new TestMessageSerializer());
        
        var options = new Catga.Configuration.CatgaOptions();
        var builder = new CatgaServiceBuilder(services, options);
        
        // Act - 测试链式调用
        var result = builder
            .AddHostedServices(hostingOptions =>
            {
                hostingOptions.ShutdownTimeout = TimeSpan.FromSeconds(45);
            });
        
        // Assert - 应该返回同一个 builder 实例
        Assert.Same(builder, result);
        
        var serviceProvider = services.BuildServiceProvider();
        var hostingOptions = serviceProvider.GetService<HostingOptions>();
        Assert.NotNull(hostingOptions);
        Assert.Equal(TimeSpan.FromSeconds(45), hostingOptions.ShutdownTimeout);
    }
    
    /// <summary>
    /// 测试配置验证
    /// Requirements: 9.1
    /// </summary>
    [Fact]
    public void AddHostedServices_ValidatesConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostApplicationLifetime>(new TestApplicationLifetime());
        services.AddSingleton<IMessageTransport>(new TestMessageTransport());
        services.AddSingleton<Catga.Outbox.IOutboxStore>(new TestOutboxStore());
        services.AddSingleton<Catga.Abstractions.IMessageSerializer>(new TestMessageSerializer());
        
        var options = new Catga.Configuration.CatgaOptions();
        var builder = new CatgaServiceBuilder(services, options);
        
        // Act & Assert - 无效配置应该抛出异常
        Assert.Throws<ArgumentException>(() =>
        {
            builder.AddHostedServices(hostingOptions =>
            {
                hostingOptions.ShutdownTimeout = TimeSpan.FromSeconds(-1); // 无效值
            });
        });
    }
    
    // 测试辅助类
    private class TestApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _startedSource = new();
        private readonly CancellationTokenSource _stoppingSource = new();
        private readonly CancellationTokenSource _stoppedSource = new();

        public CancellationToken ApplicationStarted => _startedSource.Token;
        public CancellationToken ApplicationStopping => _stoppingSource.Token;
        public CancellationToken ApplicationStopped => _stoppedSource.Token;

        public void StopApplication() => _stoppingSource.Cancel();
    }

    private class TestMessageTransport : IMessageTransport
    {
        public string Name => "TestTransport";
        public BatchTransportOptions? BatchOptions => null;
        public CompressionTransportOptions? CompressionOptions => null;

        public Task PublishAsync<TMessage>(TMessage message, TransportContext? context = null, CancellationToken cancellationToken = default)
            where TMessage : class => Task.CompletedTask;

        public Task SendAsync<TMessage>(TMessage message, string destination, TransportContext? context = null, CancellationToken cancellationToken = default)
            where TMessage : class => Task.CompletedTask;

        public Task SubscribeAsync<TMessage>(Func<TMessage, TransportContext, Task> handler, CancellationToken cancellationToken = default)
            where TMessage : class => Task.CompletedTask;

        public Task PublishBatchAsync<TMessage>(IEnumerable<TMessage> messages, TransportContext? context = null, CancellationToken cancellationToken = default)
            where TMessage : class => Task.CompletedTask;

        public Task SendBatchAsync<TMessage>(IEnumerable<TMessage> messages, string destination, TransportContext? context = null, CancellationToken cancellationToken = default)
            where TMessage : class => Task.CompletedTask;
    }

    private class TestOutboxStore : Catga.Outbox.IOutboxStore
    {
        public ValueTask AddAsync(Catga.Outbox.OutboxMessage message, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask<IReadOnlyList<Catga.Outbox.OutboxMessage>> GetPendingMessagesAsync(
            int maxCount = 100,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<Catga.Outbox.OutboxMessage>>(Array.Empty<Catga.Outbox.OutboxMessage>());

        public ValueTask MarkAsPublishedAsync(long messageId, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask MarkAsFailedAsync(long messageId, string errorMessage, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask DeletePublishedMessagesAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private class TestMessageSerializer : Catga.Abstractions.IMessageSerializer
    {
        public string Name => "TestSerializer";
        
        public byte[] Serialize<T>(T value) => Array.Empty<byte>();
        public T Deserialize<T>(byte[] data) => default!;
        public T Deserialize<T>(ReadOnlySpan<byte> data) => default!;
        public void Serialize<T>(T value, IBufferWriter<byte> bufferWriter) { }
        public byte[] Serialize(object value, Type type) => Array.Empty<byte>();
        public object? Deserialize(byte[] data, Type type) => new object();
        public object? Deserialize(ReadOnlySpan<byte> data, Type type) => new object();
        public void Serialize(object value, Type type, IBufferWriter<byte> bufferWriter) { }
    }
}
