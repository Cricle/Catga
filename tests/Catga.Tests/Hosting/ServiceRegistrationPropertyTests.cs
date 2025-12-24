using Catga.DependencyInjection;
using Catga.Hosting;
using Catga.Transport;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Catga.Tests.Hosting;

/// <summary>
/// 服务注册属性测试
/// Feature: hosting-integration
/// </summary>
public class ServiceRegistrationPropertyTests
{
    /// <summary>
    /// Property 17: 自动注册必需服务
    /// For any Catga 配置，调用 AddHostedServices() 后，所有必需的托管服务应该被自动注册到服务容器中
    /// Validates: Requirements 9.2
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property17_AddHostedServices_AutomaticallyRegistersRequiredServices(
        bool enableAutoRecovery,
        bool enableTransportHosting,
        bool enableOutboxProcessor)
    {
        return Prop.ForAll(
            Gen.Constant((enableAutoRecovery, enableTransportHosting, enableOutboxProcessor)).ToArbitrary(),
            config =>
            {
                // Arrange
                var services = new ServiceCollection();
                
                // 添加必需的依赖
                services.AddLogging();
                services.AddSingleton<IHostApplicationLifetime>(new TestApplicationLifetime());
                
                // 添加模拟的传输层
                services.AddSingleton<IMessageTransport>(new TestMessageTransport());
                
                // 添加模拟的 Outbox Store
                services.AddSingleton<Catga.Outbox.IOutboxStore>(new TestOutboxStore());
                
                // 创建 Catga 服务构建器
                var options = new Catga.Configuration.CatgaOptions();
                var builder = new CatgaServiceBuilder(services, options);
                
                // Act - 添加托管服务
                builder.AddHostedServices(hostingOptions =>
                {
                    hostingOptions.EnableAutoRecovery = config.enableAutoRecovery;
                    hostingOptions.EnableTransportHosting = config.enableTransportHosting;
                    hostingOptions.EnableOutboxProcessor = config.enableOutboxProcessor;
                });
                
                var serviceProvider = services.BuildServiceProvider();
                
                // Assert - 验证配置选项已注册
                var hostingOptions = serviceProvider.GetService<HostingOptions>();
                var optionsRegistered = hostingOptions != null;
                var optionsMatch = hostingOptions != null &&
                    hostingOptions.EnableAutoRecovery == config.enableAutoRecovery &&
                    hostingOptions.EnableTransportHosting == config.enableTransportHosting &&
                    hostingOptions.EnableOutboxProcessor == config.enableOutboxProcessor;
                
                // 验证托管服务已注册
                var hostedServices = serviceProvider.GetServices<IHostedService>().ToList();
                
                var recoveryServiceRegistered = !config.enableAutoRecovery ||
                    hostedServices.Any(s => s is RecoveryHostedService);
                
                var recoveryOptionsRegistered = !config.enableAutoRecovery ||
                    serviceProvider.GetService<RecoveryOptions>() != null;
                
                var transportServiceRegistered = !config.enableTransportHosting ||
                    hostedServices.Any(s => s is TransportHostedService);
                
                var outboxServiceRegistered = !config.enableOutboxProcessor ||
                    hostedServices.Any(s => s is OutboxProcessorService);
                
                var outboxOptionsRegistered = !config.enableOutboxProcessor ||
                    serviceProvider.GetService<OutboxProcessorOptions>() != null;
                
                var allChecks = optionsRegistered && optionsMatch &&
                    recoveryServiceRegistered && recoveryOptionsRegistered &&
                    transportServiceRegistered &&
                    outboxServiceRegistered && outboxOptionsRegistered;
                
                return allChecks.Label(
                    $"Services registered correctly for config: " +
                    $"AutoRecovery={config.enableAutoRecovery}, " +
                    $"TransportHosting={config.enableTransportHosting}, " +
                    $"OutboxProcessor={config.enableOutboxProcessor}. " +
                    $"Checks: Options={optionsRegistered && optionsMatch}, " +
                    $"Recovery={recoveryServiceRegistered && recoveryOptionsRegistered}, " +
                    $"Transport={transportServiceRegistered}, " +
                    $"Outbox={outboxServiceRegistered && outboxOptionsRegistered}");
            });
    }

    /// <summary>
    /// Property 18: 默认配置有效性
    /// For any 未显式配置的选项，使用默认值应该能够成功启动应用程序并正常运行
    /// Validates: Requirements 9.4
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property18_DefaultConfiguration_IsValid()
    {
        return Prop.ForAll(
            Gen.Constant(true).ToArbitrary(),
            _ =>
            {
                // Arrange
                var services = new ServiceCollection();
                
                // 添加必需的依赖
                services.AddLogging();
                services.AddSingleton<IHostApplicationLifetime>(new TestApplicationLifetime());
                services.AddSingleton<IMessageTransport>(new TestMessageTransport());
                services.AddSingleton<Catga.Outbox.IOutboxStore>(new TestOutboxStore());
                
                // 创建 Catga 服务构建器
                var options = new Catga.Configuration.CatgaOptions();
                var builder = new CatgaServiceBuilder(services, options);
                
                // Act - 使用默认配置添加托管服务（不传递任何配置）
                builder.AddHostedServices();
                
                var serviceProvider = services.BuildServiceProvider();
                
                // Assert - 验证默认配置已注册且有效
                var hostingOptions = serviceProvider.GetService<HostingOptions>();
                var optionsValid = hostingOptions != null;
                
                // 验证默认值
                var defaultsCorrect = hostingOptions != null &&
                    hostingOptions.EnableAutoRecovery == true &&  // 默认启用
                    hostingOptions.EnableTransportHosting == true &&  // 默认启用
                    hostingOptions.EnableOutboxProcessor == true &&  // 默认启用
                    hostingOptions.ShutdownTimeout == TimeSpan.FromSeconds(30);  // 默认 30 秒
                
                // 验证 RecoveryOptions 默认值
                var recoveryOptions = serviceProvider.GetService<RecoveryOptions>();
                var recoveryDefaultsCorrect = recoveryOptions != null &&
                    recoveryOptions.CheckInterval == TimeSpan.FromSeconds(30) &&
                    recoveryOptions.MaxRetries == 3 &&
                    recoveryOptions.RetryDelay == TimeSpan.FromSeconds(5) &&
                    recoveryOptions.EnableAutoRecovery == true &&
                    recoveryOptions.UseExponentialBackoff == true;
                
                // 验证 OutboxProcessorOptions 默认值
                var outboxOptions = serviceProvider.GetService<OutboxProcessorOptions>();
                var outboxDefaultsCorrect = outboxOptions != null &&
                    outboxOptions.ScanInterval == TimeSpan.FromSeconds(5) &&
                    outboxOptions.BatchSize == 100 &&
                    outboxOptions.ErrorDelay == TimeSpan.FromSeconds(10) &&
                    outboxOptions.CompleteCurrentBatchOnShutdown == true;
                
                // 验证所有托管服务都已注册
                var hostedServices = serviceProvider.GetServices<IHostedService>().ToList();
                var allServicesRegistered =
                    hostedServices.Any(s => s is RecoveryHostedService) &&
                    hostedServices.Any(s => s is TransportHostedService) &&
                    hostedServices.Any(s => s is OutboxProcessorService);
                
                // 尝试验证配置（不应抛出异常）
                var validationPassed = true;
                try
                {
                    hostingOptions?.Validate();
                }
                catch
                {
                    validationPassed = false;
                }
                
                var allChecks = optionsValid && defaultsCorrect &&
                    recoveryDefaultsCorrect && outboxDefaultsCorrect &&
                    allServicesRegistered && validationPassed;
                
                return allChecks.Label(
                    $"Default configuration is valid. " +
                    $"Options={optionsValid}, Defaults={defaultsCorrect}, " +
                    $"RecoveryDefaults={recoveryDefaultsCorrect}, " +
                    $"OutboxDefaults={outboxDefaultsCorrect}, " +
                    $"AllServices={allServicesRegistered}, " +
                    $"Validation={validationPassed}");
            });
    }

    /// <summary>
    /// 测试应用程序生命周期
    /// </summary>
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

    /// <summary>
    /// 测试消息传输
    /// </summary>
    private class TestMessageTransport : IMessageTransport
    {
        public string Name => "TestTransport";
        public BatchTransportOptions? BatchOptions => null;
        public CompressionTransportOptions? CompressionOptions => null;

        public Task PublishAsync<TMessage>(TMessage message, TransportContext? context = null, CancellationToken cancellationToken = default)
            where TMessage : class
        {
            return Task.CompletedTask;
        }

        public Task SendAsync<TMessage>(TMessage message, string destination, TransportContext? context = null, CancellationToken cancellationToken = default)
            where TMessage : class
        {
            return Task.CompletedTask;
        }

        public Task SubscribeAsync<TMessage>(Func<TMessage, TransportContext, Task> handler, CancellationToken cancellationToken = default)
            where TMessage : class
        {
            return Task.CompletedTask;
        }

        public Task PublishBatchAsync<TMessage>(IEnumerable<TMessage> messages, TransportContext? context = null, CancellationToken cancellationToken = default)
            where TMessage : class
        {
            return Task.CompletedTask;
        }

        public Task SendBatchAsync<TMessage>(IEnumerable<TMessage> messages, string destination, TransportContext? context = null, CancellationToken cancellationToken = default)
            where TMessage : class
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 测试 Outbox Store
    /// </summary>
    private class TestOutboxStore : Catga.Outbox.IOutboxStore
    {
        public ValueTask AddAsync(Catga.Outbox.OutboxMessage message, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<Catga.Outbox.OutboxMessage>> GetPendingMessagesAsync(
            int maxCount = 100,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IReadOnlyList<Catga.Outbox.OutboxMessage>>(
                Array.Empty<Catga.Outbox.OutboxMessage>());
        }

        public ValueTask MarkAsPublishedAsync(long messageId, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask MarkAsFailedAsync(
            long messageId,
            string errorMessage,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask DeletePublishedMessagesAsync(
            TimeSpan retentionPeriod,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }
}
