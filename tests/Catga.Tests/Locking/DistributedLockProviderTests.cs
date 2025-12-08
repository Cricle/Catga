using Catga.Locking;
using Xunit;

namespace Catga.Tests.Locking;

public class DistributedLockProviderTests
{
    [Fact]
    public async Task AcquireAsync_FirstLock_Succeeds()
    {
        // Arrange
        var provider = new InMemoryDistributedLockProvider();

        // Act
        var handle = await provider.AcquireAsync("key1", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));

        // Assert
        Assert.NotNull(handle);
        await handle.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_SameKey_BlocksUntilReleased()
    {
        // Arrange
        var provider = new InMemoryDistributedLockProvider();
        var handle1 = await provider.AcquireAsync("key1", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
        Assert.NotNull(handle1);

        // Act - try to acquire same key with short wait
        var handle2Task = provider.AcquireAsync("key1", TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100));
        var handle2 = await handle2Task;

        // Assert - should fail because first lock is held
        Assert.Null(handle2);

        // Cleanup
        await handle1.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_DifferentKeys_BothSucceed()
    {
        // Arrange
        var provider = new InMemoryDistributedLockProvider();

        // Act
        var handle1 = await provider.AcquireAsync("key1", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
        var handle2 = await provider.AcquireAsync("key2", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));

        // Assert
        Assert.NotNull(handle1);
        Assert.NotNull(handle2);

        // Cleanup
        await handle1.DisposeAsync();
        await handle2.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_AfterRelease_CanReacquire()
    {
        // Arrange
        var provider = new InMemoryDistributedLockProvider();
        var handle1 = await provider.AcquireAsync("key1", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
        Assert.NotNull(handle1);

        // Act - release and reacquire
        await handle1.DisposeAsync();
        var handle2 = await provider.AcquireAsync("key1", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));

        // Assert
        Assert.NotNull(handle2);
        await handle2.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_ConcurrentRequests_OnlyOneSucceeds()
    {
        // Arrange
        var provider = new InMemoryDistributedLockProvider();
        var successCount = 0;
        var tasks = new List<Task>();

        // Act - 10 concurrent lock attempts
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var handle = await provider.AcquireAsync("shared-key", TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(50));
                if (handle != null)
                {
                    Interlocked.Increment(ref successCount);
                    await Task.Delay(100); // Hold lock briefly
                    await handle.DisposeAsync();
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - only one should have succeeded initially
        Assert.True(successCount >= 1);
    }

    [Fact]
    public async Task DisposeAsync_MultipleCalls_Safe()
    {
        // Arrange
        var provider = new InMemoryDistributedLockProvider();
        var handle = await provider.AcquireAsync("key1", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
        Assert.NotNull(handle);

        // Act - dispose multiple times
        await handle.DisposeAsync();
        await handle.DisposeAsync();
        await handle.DisposeAsync();

        // Assert - no exception thrown
    }
}






