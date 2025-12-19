using System.Runtime.CompilerServices;

namespace Catga.Core;

/// <summary>
/// Helper for batch parallel operations with chunking to prevent thread pool starvation.
/// </summary>
internal static class BatchOperationHelper
{
    public const int DefaultChunkSize = 100;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task ExecuteBatchAsync<T>(
        IEnumerable<T> items,
        Func<T, Task> operation,
        int chunkSize = DefaultChunkSize)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(operation);

        if (items is ICollection<T> collection)
        {
            if (collection.Count == 0) return Task.CompletedTask;

            if (collection.Count <= chunkSize || chunkSize <= 0)
            {
                var tasks = new Task[collection.Count];
                var index = 0;
                foreach (var item in collection)
                    tasks[index++] = operation(item);
                return Task.WhenAll(tasks);
            }

            return ExecuteChunkedAsync(collection, operation, chunkSize);
        }

        return ExecuteBatchSlowPathAsync(items, operation, chunkSize);
    }

    private static async Task ExecuteBatchSlowPathAsync<T>(
        IEnumerable<T> items,
        Func<T, Task> operation,
        int chunkSize)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0) return;

        if (itemList.Count <= chunkSize || chunkSize <= 0)
        {
            var tasks = new Task[itemList.Count];
            for (var i = 0; i < itemList.Count; i++)
                tasks[i] = operation(itemList[i]);
            await Task.WhenAll(tasks).ConfigureAwait(false);
            return;
        }

        await ExecuteChunkedAsync(itemList, operation, chunkSize).ConfigureAwait(false);
    }

    private static async Task ExecuteChunkedAsync<T>(
        ICollection<T> items,
        Func<T, Task> operation,
        int chunkSize)
    {
        var itemList = items as IList<T> ?? items.ToList();
        var totalCount = itemList.Count;

        for (var i = 0; i < totalCount; i += chunkSize)
        {
            var end = Math.Min(i + chunkSize, totalCount);
            var chunkTasks = new Task[end - i];

            for (var j = i; j < end; j++)
                chunkTasks[j - i] = operation(itemList[j]);

            await Task.WhenAll(chunkTasks).ConfigureAwait(false);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task ExecuteBatchAsync<T, TParam>(
        IEnumerable<T> items,
        TParam parameter,
        Func<T, TParam, Task> operation,
        int chunkSize = DefaultChunkSize)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(operation);

        if (items is ICollection<T> collection)
        {
            if (collection.Count == 0) return Task.CompletedTask;

            if (collection.Count <= chunkSize || chunkSize <= 0)
            {
                var tasks = new Task[collection.Count];
                var index = 0;
                foreach (var item in collection)
                    tasks[index++] = operation(item, parameter);
                return Task.WhenAll(tasks);
            }

            return ExecuteChunkedAsync(collection, parameter, operation, chunkSize);
        }

        return ExecuteBatchSlowPathAsync(items, parameter, operation, chunkSize);
    }

    private static async Task ExecuteBatchSlowPathAsync<T, TParam>(
        IEnumerable<T> items,
        TParam parameter,
        Func<T, TParam, Task> operation,
        int chunkSize)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0) return;

        if (itemList.Count <= chunkSize || chunkSize <= 0)
        {
            var tasks = new Task[itemList.Count];
            for (var i = 0; i < itemList.Count; i++)
                tasks[i] = operation(itemList[i], parameter);
            await Task.WhenAll(tasks).ConfigureAwait(false);
            return;
        }

        await ExecuteChunkedAsync(itemList, parameter, operation, chunkSize).ConfigureAwait(false);
    }

    private static async Task ExecuteChunkedAsync<T, TParam>(
        ICollection<T> items,
        TParam parameter,
        Func<T, TParam, Task> operation,
        int chunkSize)
    {
        var itemList = items as IList<T> ?? items.ToList();
        var totalCount = itemList.Count;

        for (var i = 0; i < totalCount; i += chunkSize)
        {
            var end = Math.Min(i + chunkSize, totalCount);
            var chunkTasks = new Task[end - i];

            for (var j = i; j < end; j++)
                chunkTasks[j - i] = operation(itemList[j], parameter);

            await Task.WhenAll(chunkTasks).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Execute batch operations with concurrency control using SemaphoreSlim.
    /// </summary>
    public static async Task ExecuteConcurrentBatchAsync<T>(
        IEnumerable<T> items,
        Func<T, Task> operation,
        int maxConcurrency,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxConcurrency, 0);

        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = items is ICollection<T> c ? new List<Task>(c.Count) : new List<Task>();

        foreach (var item in items)
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            tasks.Add(ExecuteWithSemaphoreAsync(item, operation, semaphore));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task ExecuteWithSemaphoreAsync<T>(T item, Func<T, Task> operation, SemaphoreSlim semaphore)
    {
        try { await operation(item).ConfigureAwait(false); }
        finally { semaphore.Release(); }
    }
}
