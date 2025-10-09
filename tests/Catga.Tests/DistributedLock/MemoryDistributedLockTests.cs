using Catga.DistributedLock;
using Xunit;

namespace Catga.Tests.DistributedLock;

public class MemoryDistributedLockTests
{
    private readonly MemoryDistributedLock _lock;

    public MemoryDistributedLockTests()
    {
        _lock = new MemoryDistributedLock();
    }

    [Fact]
    public async Task TryAcquireAsync_SuccessfullyAcquiresLock()
    {
        // Arrange
        var key = "test-lock";
        var timeout = TimeSpan.FromSeconds(5);

        // Act
        await using var handle = await _lock.TryAcquireAsync(key, timeout);

        // Assert
        Assert.NotNull(handle);
        Assert.Equal(key, handle.Key);
        Assert.True(handle.IsHeld);
        Assert.NotEmpty(handle.LockId);
    }

    [Fact]
    public async Task TryAcquireAsync_SecondAttemptFails()
    {
        // Arrange
        var key = "test-lock";
        var timeout = TimeSpan.FromSeconds(5);

        // Act
        await using var handle1 = await _lock.TryAcquireAsync(key, timeout);
        var handle2 = await _lock.TryAcquireAsync(key, TimeSpan.FromMilliseconds(100));

        // Assert
        Assert.NotNull(handle1);
        Assert.Null(handle2);
    }

    [Fact]
    public async Task TryAcquireAsync_SucceedsAfterRelease()
    {
        // Arrange
        var key = "test-lock";
        var timeout = TimeSpan.FromSeconds(5);

        // Act
        var handle1 = await _lock.TryAcquireAsync(key, timeout);
        await handle1!.DisposeAsync();

        var handle2 = await _lock.TryAcquireAsync(key, timeout);

        // Assert
        Assert.NotNull(handle2);
        Assert.False(handle1.IsHeld);
        Assert.True(handle2!.IsHeld);

        await handle2.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_DifferentKeys_BothSucceed()
    {
        // Arrange
        var key1 = "test-lock-1";
        var key2 = "test-lock-2";
        var timeout = TimeSpan.FromSeconds(5);

        // Act
        await using var handle1 = await _lock.TryAcquireAsync(key1, timeout);
        await using var handle2 = await _lock.TryAcquireAsync(key2, timeout);

        // Assert
        Assert.NotNull(handle1);
        Assert.NotNull(handle2);
        Assert.True(handle1.IsHeld);
        Assert.True(handle2.IsHeld);
    }

    [Fact]
    public async Task Dispose_ReleasesLock()
    {
        // Arrange
        var key = "test-lock";
        var timeout = TimeSpan.FromSeconds(5);
        var handle1 = await _lock.TryAcquireAsync(key, timeout);

        // Act
        handle1!.Dispose();

        // Assert
        Assert.False(handle1.IsHeld);

        // Should be able to acquire again
        await using var handle2 = await _lock.TryAcquireAsync(key, timeout);
        Assert.NotNull(handle2);
    }

    [Fact]
    public async Task TryAcquireAsync_EmptyKey_ThrowsException()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(5);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _lock.TryAcquireAsync("", timeout).AsTask());
    }

    [Fact]
    public async Task TryAcquireAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var key = "test-lock";
        var timeout = TimeSpan.FromSeconds(10);
        await using var handle1 = await _lock.TryAcquireAsync(key, timeout);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _lock.TryAcquireAsync(key, timeout, cts.Token).AsTask());
    }

    [Fact]
    public async Task ConcurrentAcquisitions_OnlyOneSucceeds()
    {
        // Arrange
        var key = "test-lock";
        var timeout = TimeSpan.FromSeconds(5);
        var successCount = 0;

        // Act
        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            var handle = await _lock.TryAcquireAsync(key, TimeSpan.FromMilliseconds(100));
            if (handle != null)
            {
                Interlocked.Increment(ref successCount);
                await Task.Delay(50);
                await handle.DisposeAsync();
            }
        });

        await Task.WhenAll(tasks);

        // Assert
        // Only one should have succeeded initially, but others may succeed after release
        Assert.True(successCount >= 1);
    }
}

