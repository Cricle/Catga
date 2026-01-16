using Catga.Cluster;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Catga.Tests.Cluster;

public sealed class SingletonTaskRunnerTests
{
    private sealed class TestSingletonTask : SingletonTaskRunner
    {
        private readonly TaskCompletionSource<bool> _executionStarted = new();
        private readonly TaskCompletionSource<bool> _shouldComplete = new();

        public TestSingletonTask(IClusterCoordinator coordinator)
            : base(coordinator, NullLogger<TestSingletonTask>.Instance, TimeSpan.FromMilliseconds(50))
        {
        }

        protected override string TaskName => "TestTask";

        public Task WaitForExecutionStartAsync() => _executionStarted.Task;
        public void CompleteExecution() => _shouldComplete.SetResult(true);

        protected override async Task ExecuteLeaderTaskAsync(CancellationToken stoppingToken)
        {
            _executionStarted.SetResult(true);
            await _shouldComplete.Task.WaitAsync(stoppingToken);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenLeader_StartsTask()
    {
        // Arrange
        var mockCoordinator = new Mock<IClusterCoordinator>();
        mockCoordinator.Setup(c => c.IsLeader).Returns(true);
        mockCoordinator.Setup(c => c.NodeId).Returns("node1");
        mockCoordinator
            .Setup(c => c.ExecuteIfLeaderAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>(async (action, ct) =>
            {
                await action(ct);
                return true;
            });

        var task = new TestSingletonTask(mockCoordinator.Object);
        using var cts = new CancellationTokenSource();

        // Act
        var runTask = task.StartAsync(cts.Token);
        var executionStarted = await Task.WhenAny(task.WaitForExecutionStartAsync(), Task.Delay(1000));

        // Assert
        Assert.Equal(task.WaitForExecutionStartAsync(), executionStarted);

        // Cleanup
        task.CompleteExecution();
        cts.Cancel();
        await task.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNotLeader_DoesNotStartTask()
    {
        // Arrange
        var mockCoordinator = new Mock<IClusterCoordinator>();
        mockCoordinator.Setup(c => c.IsLeader).Returns(false);
        mockCoordinator.Setup(c => c.NodeId).Returns("node1");

        var task = new TestSingletonTask(mockCoordinator.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        var runTask = task.StartAsync(cts.Token);
        var executionStarted = await Task.WhenAny(task.WaitForExecutionStartAsync(), Task.Delay(150));

        // Assert - task should not start
        Assert.NotEqual(task.WaitForExecutionStartAsync(), executionStarted);

        // Cleanup
        await task.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenLeadershipLost_StopsTask()
    {
        // Arrange
        var isLeader = true;
        var mockCoordinator = new Mock<IClusterCoordinator>();
        mockCoordinator.Setup(c => c.IsLeader).Returns(() => isLeader);
        mockCoordinator.Setup(c => c.NodeId).Returns("node1");
        mockCoordinator
            .Setup(c => c.ExecuteIfLeaderAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>(async (action, ct) =>
            {
                if (!isLeader) return false;
                try
                {
                    await action(ct);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            });

        var task = new TestSingletonTask(mockCoordinator.Object);
        using var cts = new CancellationTokenSource();

        // Act
        var runTask = task.StartAsync(cts.Token);
        await task.WaitForExecutionStartAsync();

        // Simulate leadership loss
        isLeader = false;
        task.CompleteExecution();

        // Give time for task to detect leadership loss
        await Task.Delay(100);

        // Cleanup
        cts.Cancel();
        await task.StopAsync(CancellationToken.None);

        // Assert - task should have stopped
        Assert.True(task.WaitForExecutionStartAsync().IsCompleted);
    }
}
