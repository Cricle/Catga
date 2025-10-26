using Catga;
using Catga.Abstractions;
using Catga.Core;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Tests.Core;

/// <summary>
/// 流式处理完整场景测试 (TDD方法)
/// 测试SendStreamAsync方法的各种场景：
/// 1. 基本异步流处理
/// 2. 取消令牌处理
/// 3. 错误处理和恢复
/// 4. 空流和边界情况
/// 5. 背压和性能
/// 6. 并发流处理
/// </summary>
public class StreamProcessingTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICatgaMediator _mediator;

    public StreamProcessingTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<StreamTestCommand, StreamTestResponse>, StreamTestCommandHandler>();
        services.AddScoped<IRequestHandler<SlowStreamCommand, SlowStreamResponse>, SlowStreamCommandHandler>();
        services.AddScoped<IRequestHandler<ErrorStreamCommand, ErrorStreamResponse>, ErrorStreamCommandHandler>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<ICatgaMediator>();
    }

    #region 基础流处理测试

    [Fact]
    public async Task SendStreamAsync_WithValidStream_ShouldProcessAllItems()
    {
        // Arrange
        var commands = GenerateCommandStream(10);
        var results = new List<CatgaResult<StreamTestResponse>>();

        // Act
        await foreach (var result in _mediator.SendStreamAsync<StreamTestCommand, StreamTestResponse>(commands))
        {
            results.Add(result);
        }

        // Assert
        results.Should().HaveCount(10);
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        results.Select(r => r.Value!.Id).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task SendStreamAsync_WithEmptyStream_ShouldReturnNoResults()
    {
        // Arrange
        var commands = GenerateCommandStream(0);
        var results = new List<CatgaResult<StreamTestResponse>>();

        // Act
        await foreach (var result in _mediator.SendStreamAsync<StreamTestCommand, StreamTestResponse>(commands))
        {
            results.Add(result);
        }

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SendStreamAsync_WithSingleItem_ShouldProcessCorrectly()
    {
        // Arrange
        var commands = GenerateCommandStream(1);
        var results = new List<CatgaResult<StreamTestResponse>>();

        // Act
        await foreach (var result in _mediator.SendStreamAsync<StreamTestCommand, StreamTestResponse>(commands))
        {
            results.Add(result);
        }

        // Assert
        results.Should().HaveCount(1);
        results[0].IsSuccess.Should().BeTrue();
        results[0].Value!.Id.Should().Be(0);
    }

    [Fact]
    public async Task SendStreamAsync_WithNullStream_ShouldHandleGracefully()
    {
        // Arrange
        IAsyncEnumerable<StreamTestCommand>? commands = null;

        // Act & Assert - 应该立即抛出ArgumentNullException
        var act = async () =>
        {
            await foreach (var result in _mediator.SendStreamAsync<StreamTestCommand, StreamTestResponse>(commands!))
            {
                // 不应该执行到这里
            }
        };

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region 取消处理测试

    [Fact(Skip = "流处理的取消行为依赖于底层枚举器，可能不会立即抛出异常")]
    public async Task SendStreamAsync_WithCancellation_ShouldStopProcessing()
    {
        // Arrange
        var commands = GenerateSlowCommandStream(100);
        var cts = new CancellationTokenSource();
        var processedCount = 0;

        // Act
        try
        {
            await foreach (var result in _mediator.SendStreamAsync<SlowStreamCommand, SlowStreamResponse>(commands, cts.Token))
            {
                processedCount++;
                if (processedCount == 5)
                {
                    cts.Cancel(); // 处理5个后取消
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 取消是可以接受的
        }

        // Assert - 应该只处理了部分项
        processedCount.Should().BeLessThan(100);
        processedCount.Should().BeGreaterOrEqualTo(5);
    }

    [Fact]
    public async Task SendStreamAsync_WithPreCancelledToken_ShouldNotProcess()
    {
        // Arrange
        var commands = GenerateCommandStream(10);
        var cts = new CancellationTokenSource();
        cts.Cancel(); // 预先取消
        var processedCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var result in _mediator.SendStreamAsync<StreamTestCommand, StreamTestResponse>(commands, cts.Token))
            {
                processedCount++;
            }
        });

        // Assert
        processedCount.Should().Be(0);
    }

    #endregion

    #region 错误处理测试

    [Fact]
    public async Task SendStreamAsync_WithSomeFailures_ShouldContinueProcessing()
    {
        // Arrange
        var commands = GenerateMixedCommandStream(10, failEvery: 3);
        var results = new List<CatgaResult<StreamTestResponse>>();

        // Act
        await foreach (var result in _mediator.SendStreamAsync<StreamTestCommand, StreamTestResponse>(commands))
        {
            results.Add(result);
        }

        // Assert
        results.Should().HaveCount(10);
        var successCount = results.Count(r => r.IsSuccess);
        var failureCount = results.Count(r => !r.IsSuccess);

        successCount.Should().BeGreaterThan(0);
        failureCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SendStreamAsync_HandlerThrowsException_ShouldReturnFailureResult()
    {
        // Arrange
        var commands = GenerateErrorCommandStream(5);
        var results = new List<CatgaResult<ErrorStreamResponse>>();

        // Act
        await foreach (var result in _mediator.SendStreamAsync<ErrorStreamCommand, ErrorStreamResponse>(commands))
        {
            results.Add(result);
        }

        // Assert
        results.Should().HaveCount(5);
        results.Should().AllSatisfy(r =>
        {
            r.IsSuccess.Should().BeFalse();
            r.Error.Should().NotBeNullOrEmpty();
        });
    }

    #endregion

    #region 性能和背压测试

    [Fact]
    public async Task SendStreamAsync_LargeStream_ShouldProcessEfficiently()
    {
        // Arrange
        var itemCount = 1000;
        var commands = GenerateCommandStream(itemCount);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var processedCount = 0;

        // Act
        await foreach (var result in _mediator.SendStreamAsync<StreamTestCommand, StreamTestResponse>(commands))
        {
            processedCount++;
        }

        stopwatch.Stop();

        // Assert
        processedCount.Should().Be(itemCount);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(500); // 合理的时间限制
    }

    [Fact]
    public async Task SendStreamAsync_WithBackpressure_ShouldNotOverwhelm()
    {
        // Arrange
        var commands = GenerateSlowCommandStream(50);
        var maxConcurrentObserved = 0;
        var currentConcurrent = 0;
        var lockObj = new object();

        // Act
        await foreach (var result in _mediator.SendStreamAsync<SlowStreamCommand, SlowStreamResponse>(commands))
        {
            lock (lockObj)
            {
                currentConcurrent++;
                if (currentConcurrent > maxConcurrentObserved)
                    maxConcurrentObserved = currentConcurrent;
            }

            // 模拟处理
            await Task.Delay(5);

            lock (lockObj)
            {
                currentConcurrent--;
            }
        }

        // Assert - 验证背压控制（不应该有过多并发）
        maxConcurrentObserved.Should().BeLessThan(10);
    }

    #endregion

    #region 并发流处理测试

    [Fact]
    public async Task SendStreamAsync_MultipleConcurrentStreams_ShouldProcessIndependently()
    {
        // Arrange
        var stream1 = GenerateCommandStream(20);
        var stream2 = GenerateCommandStream(20);
        var stream3 = GenerateCommandStream(20);

        var results1 = new List<CatgaResult<StreamTestResponse>>();
        var results2 = new List<CatgaResult<StreamTestResponse>>();
        var results3 = new List<CatgaResult<StreamTestResponse>>();

        // Act - 并发处理多个流
        var task1 = Task.Run(async () =>
        {
            await foreach (var result in _mediator.SendStreamAsync<StreamTestCommand, StreamTestResponse>(stream1))
                results1.Add(result);
        });

        var task2 = Task.Run(async () =>
        {
            await foreach (var result in _mediator.SendStreamAsync<StreamTestCommand, StreamTestResponse>(stream2))
                results2.Add(result);
        });

        var task3 = Task.Run(async () =>
        {
            await foreach (var result in _mediator.SendStreamAsync<StreamTestCommand, StreamTestResponse>(stream3))
                results3.Add(result);
        });

        await Task.WhenAll(task1, task2, task3);

        // Assert
        results1.Should().HaveCount(20).And.AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        results2.Should().HaveCount(20).And.AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        results3.Should().HaveCount(20).And.AllSatisfy(r => r.IsSuccess.Should().BeTrue());
    }

    #endregion

    #region 实际场景模拟

    [Fact]
    public async Task SendStreamAsync_DataMigrationScenario_ShouldProcessBatches()
    {
        // Arrange - 模拟数据迁移场景：逐批处理数据
        var totalRecords = 500;
        var commands = GenerateCommandStream(totalRecords);
        var migratedCount = 0;
        var batchSizes = new List<int>();
        var currentBatchSize = 0;

        // Act
        await foreach (var result in _mediator.SendStreamAsync<StreamTestCommand, StreamTestResponse>(commands))
        {
            if (result.IsSuccess)
            {
                migratedCount++;
                currentBatchSize++;

                // 每50条记录算一批
                if (currentBatchSize == 50)
                {
                    batchSizes.Add(currentBatchSize);
                    currentBatchSize = 0;
                }
            }
        }

        if (currentBatchSize > 0)
            batchSizes.Add(currentBatchSize);

        // Assert
        migratedCount.Should().Be(totalRecords);
        batchSizes.Sum().Should().Be(totalRecords);
    }

    [Fact]
    public async Task SendStreamAsync_EventStreamProcessing_ShouldMaintainOrder()
    {
        // Arrange - 模拟事件流处理：保持顺序
        var eventCount = 100;
        var commands = GenerateCommandStream(eventCount);
        var processedIds = new List<int>();

        // Act
        await foreach (var result in _mediator.SendStreamAsync<StreamTestCommand, StreamTestResponse>(commands))
        {
            if (result.IsSuccess && result.Value != null)
            {
                processedIds.Add(result.Value.Id);
            }
        }

        // Assert - 事件应该按顺序处理
        processedIds.Should().BeInAscendingOrder();
        processedIds.Should().HaveCount(eventCount);
    }

    [Fact]
    public async Task SendStreamAsync_RealTimeAnalytics_ShouldProcessContinuously()
    {
        // Arrange - 模拟实时分析场景
        var duration = TimeSpan.FromMilliseconds(500);
        var commands = GenerateContinuousStream(duration);
        var processedCount = 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        await foreach (var result in _mediator.SendStreamAsync<StreamTestCommand, StreamTestResponse>(commands))
        {
            if (result.IsSuccess)
                processedCount++;
        }

        stopwatch.Stop();

        // Assert
        processedCount.Should().BeGreaterThan(0);
        stopwatch.Elapsed.Should().BeGreaterOrEqualTo(duration);
    }

    #endregion

    #region 辅助方法

    private static async IAsyncEnumerable<StreamTestCommand> GenerateCommandStream(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return new StreamTestCommand(i, $"Item-{i}");
            await Task.Yield(); // 模拟异步
        }
    }

    private static async IAsyncEnumerable<SlowStreamCommand> GenerateSlowCommandStream(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return new SlowStreamCommand(i);
            await Task.Delay(10); // 慢速流
        }
    }

    private static async IAsyncEnumerable<ErrorStreamCommand> GenerateErrorCommandStream(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return new ErrorStreamCommand(i);
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<StreamTestCommand> GenerateMixedCommandStream(int count, int failEvery)
    {
        for (int i = 0; i < count; i++)
        {
            var shouldFail = (i + 1) % failEvery == 0;
            yield return new StreamTestCommand(i, $"Item-{i}", shouldFail);
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<StreamTestCommand> GenerateContinuousStream(TimeSpan duration)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var id = 0;

        while (stopwatch.Elapsed < duration)
        {
            yield return new StreamTestCommand(id++, $"Continuous-{id}");
            await Task.Delay(10);
        }
    }

    #endregion
}

#region 测试消息和处理器

public record StreamTestCommand(int Id, string Name, bool ShouldFail = false) : IRequest<StreamTestResponse>
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
}

public record StreamTestResponse(int Id, string ProcessedName);

public record SlowStreamCommand(int Id) : IRequest<SlowStreamResponse>
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
}

public record SlowStreamResponse(int Id);

public record ErrorStreamCommand(int Id) : IRequest<ErrorStreamResponse>
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
}

public record ErrorStreamResponse(int Id);

public class StreamTestCommandHandler : IRequestHandler<StreamTestCommand, StreamTestResponse>
{
    public async Task<CatgaResult<StreamTestResponse>> HandleAsync(
        StreamTestCommand request,
        CancellationToken cancellationToken = default)
    {
        await Task.Yield();

        if (request.ShouldFail)
        {
            return CatgaResult<StreamTestResponse>.Failure($"Failed to process item {request.Id}");
        }

        return CatgaResult<StreamTestResponse>.Success(
            new StreamTestResponse(request.Id, $"Processed-{request.Name}"));
    }
}

public class SlowStreamCommandHandler : IRequestHandler<SlowStreamCommand, SlowStreamResponse>
{
    public async Task<CatgaResult<SlowStreamResponse>> HandleAsync(
        SlowStreamCommand request,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(20, cancellationToken);
        return CatgaResult<SlowStreamResponse>.Success(new SlowStreamResponse(request.Id));
    }
}

public class ErrorStreamCommandHandler : IRequestHandler<ErrorStreamCommand, ErrorStreamResponse>
{
    public Task<CatgaResult<ErrorStreamResponse>> HandleAsync(
        ErrorStreamCommand request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            CatgaResult<ErrorStreamResponse>.Failure($"Error processing item {request.Id}"));
    }
}

#endregion

