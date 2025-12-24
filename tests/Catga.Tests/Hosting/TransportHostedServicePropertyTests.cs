using Catga.Hosting;
using Catga.Transport;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Hosting;

/// <summary>
/// TransportHostedService 属性测试
/// Feature: hosting-integration
/// </summary>
public class TransportHostedServicePropertyTests
{
    /// <summary>
    /// Property 8: 传输层停止接受新消息
    /// Feature: hosting-integration, Property 8: 传输层停止接受新消息
    /// Validates: Requirements 4.4
    /// 
    /// For any 传输服务，当 ApplicationStopping 触发后，尝试发送新消息应该失败或被拒绝。
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TransportService_StopsAcceptingMessagesOnApplicationStopping()
    {
        return Prop.ForAll(
            Arb.Default.PositiveInt(),
            messageCount =>
            {
                // 限制消息数量
                var numMessages = Math.Min(messageCount.Get, 100);

                // Arrange
                var transport = Substitute.For<IMessageTransport, IStoppable>();
                var stoppable = (IStoppable)transport;
                var lifetime = new TestApplicationLifetime();
                var logger = Substitute.For<ILogger<TransportHostedService>>();
                var options = new HostingOptions
                {
                    ShutdownTimeout = TimeSpan.FromSeconds(5)
                };

                transport.Name.Returns("TestTransport");
                
                // 初始状态：接受消息
                stoppable.IsAcceptingMessages.Returns(true);
                
                // 当调用 StopAcceptingMessages 时，更新状态
                stoppable.When(x => x.StopAcceptingMessages())
                    .Do(_ => stoppable.IsAcceptingMessages.Returns(false));

                var service = new TransportHostedService(transport, lifetime, logger, options);

                // Act
                var startTask = service.StartAsync(CancellationToken.None);
                startTask.Wait(1000);

                // 验证初始状态：应该接受消息
                var wasAcceptingBefore = stoppable.IsAcceptingMessages;

                // 触发 ApplicationStopping
                lifetime.SimulateStopping();

                // 给一点时间让事件处理完成
                Thread.Sleep(100);

                // 验证停止后状态：不应该接受消息
                var isAcceptingAfter = stoppable.IsAcceptingMessages;

                // Cleanup
                var stopTask = service.StopAsync(CancellationToken.None);
                stopTask.Wait(1000);

                // Assert
                var stoppedAccepting = wasAcceptingBefore && !isAcceptingAfter;
                
                return stoppedAccepting.Label(
                    $"Transport should stop accepting messages after ApplicationStopping. " +
                    $"Before: {wasAcceptingBefore}, After: {isAcceptingAfter}");
            });
    }

    /// <summary>
    /// Property 9: 传输层等待消息完成
    /// Feature: hosting-integration, Property 9: 传输层等待消息完成
    /// Validates: Requirements 4.5
    /// 
    /// For any 传输服务中正在处理的消息，在连接关闭前，所有这些消息应该处理完成。
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TransportService_WaitsForPendingOperationsBeforeShutdown()
    {
        return Prop.ForAll(
            Arb.Default.PositiveInt(),
            initialPendingCount =>
            {
                // 限制待处理操作数量
                var pendingOps = Math.Min(initialPendingCount.Get, 50);

                // Arrange
                var transport = Substitute.For<IMessageTransport, IWaitable>();
                var waitable = (IWaitable)transport;
                var lifetime = new TestApplicationLifetime();
                var logger = Substitute.For<ILogger<TransportHostedService>>();
                var options = new HostingOptions
                {
                    ShutdownTimeout = TimeSpan.FromSeconds(5)
                };

                transport.Name.Returns("TestTransport");

                // 模拟待处理的操作
                var currentPending = pendingOps;
                waitable.PendingOperations.Returns(_ => currentPending);

                // WaitForCompletionAsync 会逐渐减少待处理操作
                waitable.WaitForCompletionAsync(Arg.Any<CancellationToken>())
                    .Returns(async callInfo =>
                    {
                        var ct = callInfo.Arg<CancellationToken>();
                        
                        // 模拟逐步完成操作
                        while (currentPending > 0 && !ct.IsCancellationRequested)
                        {
                            await Task.Delay(10, ct);
                            currentPending--;
                        }
                    });

                var service = new TransportHostedService(transport, lifetime, logger, options);

                // Act
                var startTask = service.StartAsync(CancellationToken.None);
                startTask.Wait(1000);

                var pendingBefore = waitable.PendingOperations;

                // 停止服务
                var stopTask = service.StopAsync(CancellationToken.None);
                stopTask.Wait(10000); // 给足够时间完成

                var pendingAfter = waitable.PendingOperations;

                // Assert
                // 所有待处理操作应该完成
                var allCompleted = pendingBefore > 0 && pendingAfter == 0;
                
                // 应该调用了 WaitForCompletionAsync
                var waitCalled = waitable.ReceivedCalls()
                    .Any(call => call.GetMethodInfo().Name == nameof(IWaitable.WaitForCompletionAsync));

                return (allCompleted && waitCalled).Label(
                    $"Should wait for all {pendingBefore} pending operations to complete. " +
                    $"After: {pendingAfter}, WaitCalled: {waitCalled}");
            });
    }
}

/// <summary>
/// 测试用的应用程序生命周期
/// </summary>
internal sealed class TestApplicationLifetime : IHostApplicationLifetime
{
    private readonly CancellationTokenSource _startedSource = new();
    private readonly CancellationTokenSource _stoppingSource = new();
    private readonly CancellationTokenSource _stoppedSource = new();

    public CancellationToken ApplicationStarted => _startedSource.Token;
    public CancellationToken ApplicationStopping => _stoppingSource.Token;
    public CancellationToken ApplicationStopped => _stoppedSource.Token;

    public void StopApplication()
    {
        _stoppingSource.Cancel();
    }

    public void SimulateStarted()
    {
        _startedSource.Cancel();
    }

    public void SimulateStopping()
    {
        _stoppingSource.Cancel();
    }

    public void SimulateStopped()
    {
        _stoppedSource.Cancel();
    }
}
