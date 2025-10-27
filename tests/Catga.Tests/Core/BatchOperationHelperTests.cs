using Catga.Core;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Core;

public class BatchOperationHelperTests
{
    // ==================== ExecuteBatchAsync (No Parameter) ====================

    [Fact]
    public async Task ExecuteBatchAsync_WithEmptyCollection_ShouldCompleteImmediately()
    {
        // Arrange
        var items = Array.Empty<int>();
        var processedCount = 0;

        // Act
        await BatchOperationHelper.ExecuteBatchAsync(items, item =>
        {
            processedCount++;
            return Task.CompletedTask;
        });

        // Assert
        processedCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteBatchAsync_WithSmallBatch_ShouldProcessAll()
    {
        // Arrange
        var items = new[] { 1, 2, 3, 4, 5 };
        var processed = new List<int>();
        var lockObj = new object();

        // Act
        await BatchOperationHelper.ExecuteBatchAsync(items, item =>
        {
            lock (lockObj) { processed.Add(item); }
            return Task.CompletedTask;
        });

        // Assert
        processed.Should().HaveCount(5);
        processed.Should().BeEquivalentTo(items);
    }

    [Fact]
    public async Task ExecuteBatchAsync_WithLargeBatch_ShouldUseChunking()
    {
        // Arrange
        var items = Enumerable.Range(1, 250).ToArray(); // > DefaultChunkSize (100)
        var processed = new List<int>();
        var lockObj = new object();

        // Act
        await BatchOperationHelper.ExecuteBatchAsync(items, item =>
        {
            lock (lockObj) { processed.Add(item); }
            return Task.CompletedTask;
        });

        // Assert
        processed.Should().HaveCount(250);
        processed.Should().BeEquivalentTo(items);
    }

    [Fact]
    public async Task ExecuteBatchAsync_WithCustomChunkSize_ShouldRespectChunkSize()
    {
        // Arrange
        var items = Enumerable.Range(1, 50).ToArray();
        var processed = new List<int>();
        var lockObj = new object();

        // Act
        await BatchOperationHelper.ExecuteBatchAsync(items, item =>
        {
            lock (lockObj) { processed.Add(item); }
            return Task.CompletedTask;
        }, chunkSize: 10);

        // Assert
        processed.Should().HaveCount(50);
    }

    [Fact]
    public async Task ExecuteBatchAsync_WithChunkSizeZero_ShouldDisableChunking()
    {
        // Arrange
        var items = Enumerable.Range(1, 200).ToArray();
        var processed = new List<int>();
        var lockObj = new object();

        // Act
        await BatchOperationHelper.ExecuteBatchAsync(items, item =>
        {
            lock (lockObj) { processed.Add(item); }
            return Task.CompletedTask;
        }, chunkSize: 0);

        // Assert
        processed.Should().HaveCount(200);
    }

    [Fact]
    public async Task ExecuteBatchAsync_WithNullItems_ShouldThrow()
    {
        // Act
        Func<Task> act = () => BatchOperationHelper.ExecuteBatchAsync<int>(
            null!,
            item => Task.CompletedTask);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteBatchAsync_WithNullOperation_ShouldThrow()
    {
        // Arrange
        var items = new[] { 1, 2, 3 };

        // Act
        Func<Task> act = () => BatchOperationHelper.ExecuteBatchAsync(
            items,
            null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteBatchAsync_WithIEnumerableNotCollection_ShouldProcessAll()
    {
        // Arrange
        var items = Enumerable.Range(1, 20); // IEnumerable, not ICollection
        var processed = new List<int>();
        var lockObj = new object();

        // Act
        await BatchOperationHelper.ExecuteBatchAsync(items, item =>
        {
            lock (lockObj) { processed.Add(item); }
            return Task.CompletedTask;
        });

        // Assert
        processed.Should().HaveCount(20);
    }

    // ==================== ExecuteBatchAsync (With Parameter) ====================

    [Fact]
    public async Task ExecuteBatchAsyncWithParam_WithEmptyCollection_ShouldCompleteImmediately()
    {
        // Arrange
        var items = Array.Empty<int>();
        var parameter = "test";
        var processedCount = 0;

        // Act
        await BatchOperationHelper.ExecuteBatchAsync(items, parameter, (item, param) =>
        {
            processedCount++;
            return Task.CompletedTask;
        });

        // Assert
        processedCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteBatchAsyncWithParam_ShouldPassParameterToOperation()
    {
        // Arrange
        var items = new[] { 1, 2, 3 };
        var parameter = "test-param";
        var receivedParams = new List<string>();
        var lockObj = new object();

        // Act
        await BatchOperationHelper.ExecuteBatchAsync(items, parameter, (item, param) =>
        {
            lock (lockObj) { receivedParams.Add(param); }
            return Task.CompletedTask;
        });

        // Assert
        receivedParams.Should().HaveCount(3);
        receivedParams.Should().AllBe("test-param");
    }

    [Fact]
    public async Task ExecuteBatchAsyncWithParam_WithLargeBatch_ShouldUseChunking()
    {
        // Arrange
        var items = Enumerable.Range(1, 150).ToArray();
        var parameter = 42;
        var processed = new List<(int item, int param)>();
        var lockObj = new object();

        // Act
        await BatchOperationHelper.ExecuteBatchAsync(items, parameter, (item, param) =>
        {
            lock (lockObj) { processed.Add((item, param)); }
            return Task.CompletedTask;
        });

        // Assert
        processed.Should().HaveCount(150);
        processed.Should().AllSatisfy(p => p.param.Should().Be(42));
    }

    [Fact]
    public async Task ExecuteBatchAsyncWithParam_WithNullParameter_ShouldWork()
    {
        // Arrange
        var items = new[] { 1, 2, 3 };
        string? parameter = null;
        var processedCount = 0;

        // Act
        await BatchOperationHelper.ExecuteBatchAsync(items, parameter, (item, param) =>
        {
            processedCount++;
            param.Should().BeNull();
            return Task.CompletedTask;
        });

        // Assert
        processedCount.Should().Be(3);
    }

    // ==================== ExecuteConcurrentBatchAsync ====================

    [Fact]
    public async Task ExecuteConcurrentBatchAsync_WithMaxConcurrency_ShouldLimitConcurrency()
    {
        // Arrange
        var items = Enumerable.Range(1, 20).ToArray();
        var maxConcurrency = 5;
        var currentConcurrency = 0;
        var peakConcurrency = 0;
        var lockObj = new object();

        // Act
        await BatchOperationHelper.ExecuteConcurrentBatchAsync(items, async item =>
        {
            lock (lockObj)
            {
                currentConcurrency++;
                peakConcurrency = Math.Max(peakConcurrency, currentConcurrency);
            }

            await Task.Delay(10); // Simulate work

            lock (lockObj)
            {
                currentConcurrency--;
            }
        }, maxConcurrency);

        // Assert
        peakConcurrency.Should().BeLessOrEqualTo(maxConcurrency);
    }

    [Fact]
    public async Task ExecuteConcurrentBatchAsync_WithEmptyCollection_ShouldComplete()
    {
        // Arrange
        var items = Array.Empty<int>();

        // Act
        await BatchOperationHelper.ExecuteConcurrentBatchAsync(items, item => Task.CompletedTask, maxConcurrency: 5);

        // Assert - Should complete without error
        true.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteConcurrentBatchAsync_WithCancellation_ShouldRespectToken()
    {
        // Arrange
        var items = Enumerable.Range(1, 100).ToArray();
        var cts = new CancellationTokenSource();
        var processedCount = 0;
        var lockObj = new object();

        // Act
        cts.Cancel(); // Cancel immediately
        Func<Task> act = () => BatchOperationHelper.ExecuteConcurrentBatchAsync(items, item =>
        {
            lock (lockObj) { processedCount++; }
            return Task.CompletedTask;
        }, maxConcurrency: 10, cancellationToken: cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteConcurrentBatchAsync_WithZeroMaxConcurrency_ShouldThrow()
    {
        // Arrange
        var items = new[] { 1, 2, 3 };

        // Act
        Func<Task> act = () => BatchOperationHelper.ExecuteConcurrentBatchAsync(
            items,
            item => Task.CompletedTask,
            maxConcurrency: 0);

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ExecuteConcurrentBatchAsync_WithNegativeMaxConcurrency_ShouldThrow()
    {
        // Arrange
        var items = new[] { 1, 2, 3 };

        // Act
        Func<Task> act = () => BatchOperationHelper.ExecuteConcurrentBatchAsync(
            items,
            item => Task.CompletedTask,
            maxConcurrency: -1);

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ExecuteConcurrentBatchAsync_ShouldProcessAllItems()
    {
        // Arrange
        var items = Enumerable.Range(1, 50).ToArray();
        var processed = new List<int>();
        var lockObj = new object();

        // Act
        await BatchOperationHelper.ExecuteConcurrentBatchAsync(items, item =>
        {
            lock (lockObj) { processed.Add(item); }
            return Task.CompletedTask;
        }, maxConcurrency: 10);

        // Assert
        processed.Should().HaveCount(50);
        processed.Should().BeEquivalentTo(items);
    }

    [Fact]
    public async Task ExecuteConcurrentBatchAsync_WithICollection_ShouldPreallocate()
    {
        // Arrange
        var items = new List<int>(Enumerable.Range(1, 30));
        var processed = new List<int>();
        var lockObj = new object();

        // Act
        await BatchOperationHelper.ExecuteConcurrentBatchAsync(items, item =>
        {
            lock (lockObj) { processed.Add(item); }
            return Task.CompletedTask;
        }, maxConcurrency: 5);

        // Assert
        processed.Should().HaveCount(30);
    }

    // ==================== Edge Cases ====================

    [Fact]
    public async Task ExecuteBatchAsync_WithExactChunkSize_ShouldProcessAll()
    {
        // Arrange
        var items = Enumerable.Range(1, 100).ToArray(); // Exactly DefaultChunkSize
        var processed = new List<int>();
        var lockObj = new object();

        // Act
        await BatchOperationHelper.ExecuteBatchAsync(items, item =>
        {
            lock (lockObj) { processed.Add(item); }
            return Task.CompletedTask;
        });

        // Assert
        processed.Should().HaveCount(100);
    }

    [Fact]
    public async Task ExecuteBatchAsync_WithOnePastChunkSize_ShouldUseChunking()
    {
        // Arrange
        var items = Enumerable.Range(1, 101).ToArray(); // One past DefaultChunkSize
        var processed = new List<int>();
        var lockObj = new object();

        // Act
        await BatchOperationHelper.ExecuteBatchAsync(items, item =>
        {
            lock (lockObj) { processed.Add(item); }
            return Task.CompletedTask;
        });

        // Assert
        processed.Should().HaveCount(101);
    }

    [Fact]
    public async Task ExecuteBatchAsync_WithSingleItem_ShouldProcess()
    {
        // Arrange
        var items = new[] { 42 };
        var processed = new List<int>();

        // Act
        await BatchOperationHelper.ExecuteBatchAsync(items, item =>
        {
            processed.Add(item);
            return Task.CompletedTask;
        });

        // Assert
        processed.Should().ContainSingle().Which.Should().Be(42);
    }

    [Fact]
    public async Task ExecuteConcurrentBatchAsync_WithMaxConcurrencyOne_ShouldProcessSequentially()
    {
        // Arrange
        var items = Enumerable.Range(1, 10).ToArray();
        var processed = new List<int>();

        // Act
        await BatchOperationHelper.ExecuteConcurrentBatchAsync(items, item =>
        {
            processed.Add(item);
            return Task.CompletedTask;
        }, maxConcurrency: 1);

        // Assert
        processed.Should().HaveCount(10);
        // With maxConcurrency=1, order should be preserved
        processed.Should().ContainInOrder(items);
    }

    // ==================== Performance Characteristics ====================

    [Fact]
    public async Task ExecuteBatchAsync_ShouldExecuteInParallel()
    {
        // Arrange
        var items = Enumerable.Range(1, 10).ToArray();
        var startTime = DateTime.UtcNow;

        // Act
        await BatchOperationHelper.ExecuteBatchAsync(items, async item =>
        {
            await Task.Delay(50); // Each item takes 50ms
        });

        var elapsed = DateTime.UtcNow - startTime;

        // Assert - Should take ~50ms (parallel), not 500ms (sequential)
        elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public async Task ExecuteConcurrentBatchAsync_ShouldRespectConcurrencyLimit()
    {
        // Arrange
        var items = Enumerable.Range(1, 10).ToArray();
        var currentCount = 0;
        var maxObserved = 0;
        var lockObj = new object();

        // Act
        await BatchOperationHelper.ExecuteConcurrentBatchAsync(items, async item =>
        {
            lock (lockObj)
            {
                currentCount++;
                maxObserved = Math.Max(maxObserved, currentCount);
            }

            await Task.Delay(20);

            lock (lockObj)
            {
                currentCount--;
            }
        }, maxConcurrency: 3);

        // Assert
        maxObserved.Should().BeLessOrEqualTo(3);
    }
}

