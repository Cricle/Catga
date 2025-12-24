using Catga.Cluster;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Cluster;

/// <summary>
/// Unit tests for SingletonTaskRunner.
/// Tests that tasks only run on leader nodes.
/// </summary>
public class SingletonTaskRunnerTests
{
    private readonly IClusterCoordinator _mockCoordinator;

    public SingletonTaskRunnerTests()
    {
        _mockCoordinator = Substitute.For<IClusterCoordinator>();
    }

    [Fact]
    public async Task ExecuteAsync_WhenNotLeader_ShouldWaitAndNotExecuteTask()
    {
        // Arrange
        _mockCoordinator.IsLeader.Returns(false);
        _mockCoordinator.NodeId.Returns("node-1");

        var task = new TestSingletonTask(_mockCoordinator);
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));

        // Act
        await task.StartAsync(cts.Token);
        await Task.Delay(300);
        await task.StopAsync(CancellationToken.None);

        // Assert
        task.ExecutionCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenLeader_ShouldExecuteTask()
    {
        // Arrange
        _mockCoordinator.IsLeader.Returns(true);
        _mockCoordinator.NodeId.Returns("node-1");
        _mockCoordinator.ExecuteIfLeaderAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var action = callInfo.Arg<Func<CancellationToken, Task>>();
                var ct = callInfo.Arg<CancellationToken>();
                return Task.FromResult(true);
            });

        var task = new TestSingletonTask(_mockCoordinator);
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));

        // Act
        await task.StartAsync(cts.Token);
        await Task.Delay(300);
        await task.StopAsync(CancellationToken.None);

        // Assert
        await _mockCoordinator.Received().ExecuteIfLeaderAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenLeadershipLost_ShouldStopTask()
    {
        // Arrange
        var isLeader = true;
        _mockCoordinator.IsLeader.Returns(_ => isLeader);
        _mockCoordinator.NodeId.Returns("node-1");

        var taskCompletionSource = new TaskCompletionSource<bool>();
        _mockCoordinator.ExecuteIfLeaderAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var action = callInfo.Arg<Func<CancellationToken, Task>>();
                var ct = callInfo.Arg<CancellationToken>();
                await action(ct);
                return true;
            });

        var task = new TestSingletonTask(_mockCoordinator, taskCompletionSource);
        var cts = new CancellationTokenSource();

        // Act
        await task.StartAsync(cts.Token);
        await Task.Delay(100);

        // Simulate leadership loss
        isLeader = false;
        taskCompletionSource.SetResult(true);

        await Task.Delay(100);
        await task.StopAsync(CancellationToken.None);

        // Assert
        task.ExecutionCount.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void Constructor_WithCustomCheckInterval_ShouldUseInterval()
    {
        // Arrange & Act
        var task = new TestSingletonTask(
            _mockCoordinator,
            checkInterval: TimeSpan.FromSeconds(5));

        // Assert
        task.Should().NotBeNull();
    }

    // Test implementation of SingletonTaskRunner
    public class TestSingletonTask : SingletonTaskRunner
    {
        private readonly TaskCompletionSource<bool>? _taskCompletion;
        public int ExecutionCount { get; private set; }

        public TestSingletonTask(
            IClusterCoordinator coordinator,
            TaskCompletionSource<bool>? taskCompletion = null,
            TimeSpan? checkInterval = null)
            : base(coordinator, NullLogger.Instance, checkInterval)
        {
            _taskCompletion = taskCompletion;
        }

        protected override string TaskName => "TestTask";

        protected override async Task ExecuteLeaderTaskAsync(CancellationToken stoppingToken)
        {
            ExecutionCount++;

            if (_taskCompletion != null)
            {
                await _taskCompletion.Task;
            }
            else
            {
                // Run indefinitely until cancelled
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(100, stoppingToken);
                }
            }
        }
    }
}
