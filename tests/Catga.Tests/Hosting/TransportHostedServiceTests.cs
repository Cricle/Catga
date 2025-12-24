using Catga.Hosting;
using Catga.Transport;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Hosting;

/// <summary>
/// TransportHostedService 单元测试
/// </summary>
public class TransportHostedServiceTests
{
    private readonly IMessageTransport _transport;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<TransportHostedService> _logger;
    private readonly HostingOptions _options;

    public TransportHostedServiceTests()
    {
        _transport = Substitute.For<IMessageTransport>();
        _lifetime = new TestApplicationLifetime();
        _logger = Substitute.For<ILogger<TransportHostedService>>();
        _options = new HostingOptions
        {
            ShutdownTimeout = TimeSpan.FromSeconds(5)
        };

        _transport.Name.Returns("TestTransport");
    }

    [Fact]
    public void Constructor_WithNullTransport_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new TransportHostedService(null!, _lifetime, _logger, _options));
    }

    [Fact]
    public void Constructor_WithNullLifetime_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new TransportHostedService(_transport, null!, _logger, _options));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new TransportHostedService(_transport, _lifetime, null!, _options));
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new TransportHostedService(_transport, _lifetime, _logger, null!));
    }

    [Fact]
    public async Task StartAsync_WithInitializableTransport_InitializesConnection()
    {
        // Arrange
        var initializableTransport = Substitute.For<IMessageTransport, IAsyncInitializable>();
        initializableTransport.Name.Returns("InitializableTransport");
        var initializable = (IAsyncInitializable)initializableTransport;

        var service = new TransportHostedService(initializableTransport, _lifetime, _logger, _options);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        await initializable.Received(1).InitializeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_WithNonInitializableTransport_DoesNotThrow()
    {
        // Arrange
        var service = new TransportHostedService(_transport, _lifetime, _logger, _options);

        // Act & Assert
        await service.StartAsync(CancellationToken.None);
        // 不应该抛出异常
    }

    [Fact]
    public async Task StartAsync_RegistersApplicationStoppingHandler()
    {
        // Arrange
        var stoppableTransport = Substitute.For<IMessageTransport, IStoppable>();
        stoppableTransport.Name.Returns("StoppableTransport");
        var stoppable = (IStoppable)stoppableTransport;
        stoppable.IsAcceptingMessages.Returns(true);

        var testLifetime = new TestApplicationLifetime();
        var service = new TransportHostedService(stoppableTransport, testLifetime, _logger, _options);

        // Act
        await service.StartAsync(CancellationToken.None);
        
        // 触发 ApplicationStopping
        testLifetime.SimulateStopping();
        
        // 给一点时间让事件处理完成
        await Task.Delay(100);

        // Assert
        stoppable.Received(1).StopAcceptingMessages();
    }

    [Fact]
    public async Task StopAsync_WithWaitableTransport_WaitsForPendingOperations()
    {
        // Arrange
        var waitableTransport = Substitute.For<IMessageTransport, IWaitable>();
        waitableTransport.Name.Returns("WaitableTransport");
        var waitable = (IWaitable)waitableTransport;
        
        var pendingOps = 5;
        waitable.PendingOperations.Returns(_ => pendingOps);
        waitable.WaitForCompletionAsync(Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await Task.Delay(100);
            pendingOps = 0;
        });

        var service = new TransportHostedService(waitableTransport, _lifetime, _logger, _options);

        // Act
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        // Assert
        await waitable.Received(1).WaitForCompletionAsync(Arg.Any<CancellationToken>());
        Assert.Equal(0, pendingOps);
    }

    [Fact]
    public async Task StopAsync_WithAsyncDisposableTransport_DisposesTransport()
    {
        // Arrange
        var disposableTransport = Substitute.For<IMessageTransport, IAsyncDisposable>();
        disposableTransport.Name.Returns("DisposableTransport");
        var asyncDisposable = (IAsyncDisposable)disposableTransport;

        var service = new TransportHostedService(disposableTransport, _lifetime, _logger, _options);

        // Act
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        // Assert
        await asyncDisposable.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task StopAsync_WithSyncDisposableTransport_DisposesTransport()
    {
        // Arrange
        var disposableTransport = Substitute.For<IMessageTransport, IDisposable>();
        disposableTransport.Name.Returns("DisposableTransport");
        var disposable = (IDisposable)disposableTransport;

        var service = new TransportHostedService(disposableTransport, _lifetime, _logger, _options);

        // Act
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        // Assert
        disposable.Received(1).Dispose();
    }

    [Fact]
    public async Task StopAsync_WithShutdownTimeout_ForcesShutdown()
    {
        // Arrange
        var waitableTransport = Substitute.For<IMessageTransport, IWaitable>();
        waitableTransport.Name.Returns("SlowTransport");
        var waitable = (IWaitable)waitableTransport;
        
        waitable.PendingOperations.Returns(10); // 始终有待处理操作
        waitable.WaitForCompletionAsync(Arg.Any<CancellationToken>()).Returns(async callInfo =>
        {
            var ct = callInfo.Arg<CancellationToken>();
            // 模拟一个永远不会完成的操作
            await Task.Delay(Timeout.Infinite, ct);
        });

        var shortTimeoutOptions = new HostingOptions
        {
            ShutdownTimeout = TimeSpan.FromMilliseconds(500)
        };

        var service = new TransportHostedService(waitableTransport, _lifetime, _logger, shortTimeoutOptions);

        // Act
        await service.StartAsync(CancellationToken.None);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await service.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        // Assert
        // 应该在超时时间附近停止（给一些余量）
        Assert.True(stopwatch.ElapsedMilliseconds < 1500, 
            $"Should timeout around 500ms, but took {stopwatch.ElapsedMilliseconds}ms");
        
        // 仍然应该有待处理的操作（因为超时了）
        Assert.Equal(10, waitable.PendingOperations);
    }

    [Fact]
    public async Task StartAsync_WithInitializationFailure_ThrowsException()
    {
        // Arrange
        var initializableTransport = Substitute.For<IMessageTransport, IAsyncInitializable>();
        initializableTransport.Name.Returns("FailingTransport");
        var initializable = (IAsyncInitializable)initializableTransport;
        
        initializable.InitializeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Connection failed")));

        var service = new TransportHostedService(initializableTransport, _lifetime, _logger, _options);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StopAsync_WithDisposalFailure_ThrowsException()
    {
        // Arrange
        var disposableTransport = Substitute.For<IMessageTransport, IAsyncDisposable>();
        disposableTransport.Name.Returns("FailingDisposableTransport");
        var asyncDisposable = (IAsyncDisposable)disposableTransport;
        
        asyncDisposable.DisposeAsync()
            .Returns(ValueTask.FromException(new InvalidOperationException("Disposal failed")));

        var service = new TransportHostedService(disposableTransport, _lifetime, _logger, _options);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.StopAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ApplicationStopping_WithNonStoppableTransport_DoesNotThrow()
    {
        // Arrange
        var testLifetime = new TestApplicationLifetime();
        var service = new TransportHostedService(_transport, testLifetime, _logger, _options);

        // Act
        await service.StartAsync(CancellationToken.None);
        testLifetime.SimulateStopping();
        await Task.Delay(100);

        // Assert
        // 不应该抛出异常
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CompleteLifecycle_WithAllFeatures_WorksCorrectly()
    {
        // Arrange
        var fullFeaturedTransport = Substitute.For(
            new[] { typeof(IMessageTransport), typeof(IAsyncInitializable), typeof(IStoppable), typeof(IWaitable), typeof(IAsyncDisposable) },
            Array.Empty<object>());
        
        ((IMessageTransport)fullFeaturedTransport).Name.Returns("FullFeaturedTransport");
        
        var initializable = (IAsyncInitializable)fullFeaturedTransport;
        var stoppable = (IStoppable)fullFeaturedTransport;
        var waitable = (IWaitable)fullFeaturedTransport;
        var disposable = (IAsyncDisposable)fullFeaturedTransport;

        stoppable.IsAcceptingMessages.Returns(true);
        var pendingOps = 3;
        waitable.PendingOperations.Returns(_ => pendingOps);
        waitable.WaitForCompletionAsync(Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await Task.Delay(50);
            pendingOps = 0;
        });

        var testLifetime = new TestApplicationLifetime();
        var service = new TransportHostedService((IMessageTransport)fullFeaturedTransport, testLifetime, _logger, _options);

        // Act
        await service.StartAsync(CancellationToken.None);
        testLifetime.SimulateStopping();
        await Task.Delay(100);
        await service.StopAsync(CancellationToken.None);

        // Assert
        await initializable.Received(1).InitializeAsync(Arg.Any<CancellationToken>());
        stoppable.Received(1).StopAcceptingMessages();
        await waitable.Received(1).WaitForCompletionAsync(Arg.Any<CancellationToken>());
        await disposable.Received(1).DisposeAsync();
    }
}
